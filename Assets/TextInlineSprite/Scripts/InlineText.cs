﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Text.RegularExpressions;
using System.Text;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using System;

[ExecuteInEditMode]
public class InlineText : Text, IPointerClickHandler
{
	#region 属性
	// 用正则取  [图集ID#表情Tag] ID值==-1 ,表示为超链接
	private static readonly Regex _inputTagRegex = new Regex(@"\[(\-{0,1}\d{0,})#(.+?)\]", RegexOptions.Singleline);
    //文本表情管理器
    private InlineManager _inlineManager;
 
    //表情位置索引信息
    private List<SpriteTagInfo> _spriteInfo = new List<SpriteTagInfo>();
	//计算定点信息的缓存数组
	private readonly UIVertex[] m_TempVerts = new UIVertex[4];

    private StringBuilder _textBuilder = new StringBuilder();

    UIVertex _tempVertex = UIVertex.simpleVert;
	private List<int> _lastRenderIndexs = new List<int>();
    #region 超链接
    [System.Serializable]
    public class HrefClickEvent : UnityEvent<string,int> { }
    //点击事件监听
    public HrefClickEvent OnHrefClick = new HrefClickEvent();
    // 超链接信息列表  
    private readonly List<HrefInfo> _listHrefInfos = new List<HrefInfo>();
	#endregion

	#endregion

	[TextArea(3, 10)] [SerializeField]
	protected string _text=string.Empty;

	public override string text
	{
		get
		{
			return m_Text;
		}
		set
		{
			if (String.IsNullOrEmpty(value))
			{
				if (String.IsNullOrEmpty(m_Text))
					return;
				m_Text = "";
				SetVerticesDirty();
			}
			else if (_text != value)
			{
				m_Text = GetOutputText(value);
				//m_Text = value;
				SetVerticesDirty();
				SetLayoutDirty();
			}
#if UNITY_EDITOR
			//编辑器赋值 如果是一样的 也可以刷新一下
			else
			{
				m_Text = GetOutputText(value);
				SetVerticesDirty();
				SetLayoutDirty();
			}
#endif
			//输入字符备份
			_text = value;
		}
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		supportRichText = true;
		alignByGeometry = true;
		_inlineManager = GetComponentInParent<InlineManager>();
    }


	protected override void Start()
	{
		m_Text = GetOutputText(_text);
		SetVerticesDirty();
		SetLayoutDirty();
	}

#if UNITY_EDITOR
	protected override void OnValidate()
    {
		base.OnValidate();
		m_Text = GetOutputText(_text);
		SetVerticesDirty();
		SetLayoutDirty();
	}
#endif

	protected override void OnPopulateMesh(VertexHelper toFill)
    {
        if (font == null)
            return;
		base.OnPopulateMesh(toFill);
		
		//更新顶点位置&去掉乱码uv
		m_DisableFontTextureRebuiltCallback = true;
		int index = -1;
		//emoji 
		for (int i = 0; i < _spriteInfo.Count; i++)
		{
			index = _spriteInfo[i].Index;
			if ((index+4) < toFill.currentVertCount)
			{
				for (int j = index; j < index + 4; j++)
				{
					toFill.PopulateUIVertex(ref _tempVertex, j);
					//清理多余的乱码uv
					_tempVertex.uv0 = Vector2.zero;
					//获取quad的位置 --> 转为世界坐标
					_spriteInfo[i].Pos[j - index] =Utility.TransformPoint2World(transform,_tempVertex.position);
					toFill.SetUIVertex(_tempVertex, j);
				}

			}
		}
		// 处理超链接包围框  
		for (int i = 0; i < _listHrefInfos.Count; i++)
		{
			_listHrefInfos[i].Boxes.Clear();
			if (_listHrefInfos[i].StartIndex >= toFill.currentVertCount)
				continue;

			toFill.PopulateUIVertex(ref _tempVertex, _listHrefInfos[i].StartIndex);
			// 将超链接里面的文本顶点索引坐标加入到包围框  
			var pos = _tempVertex.position;
			var bounds = new Bounds(pos, Vector3.zero);
			for (int j = _listHrefInfos[i].StartIndex+1; j < _listHrefInfos[i].EndIndex; j++)
			{
				if (j >= toFill.currentVertCount)
				{
					break;
				}
				toFill.PopulateUIVertex(ref _tempVertex, j);
				pos = _tempVertex.position;
				if (pos.x < bounds.min.x)
				{
					// 换行重新添加包围框  
					_listHrefInfos[i].Boxes.Add(new Rect(bounds.min, bounds.size));
					bounds = new Bounds(pos, Vector3.zero);
				}
				else
				{
					bounds.Encapsulate(pos); // 扩展包围框  
				}
			}
			//添加包围盒
			_listHrefInfos[i].Boxes.Add(new Rect(bounds.min, bounds.size));
		}
		m_DisableFontTextureRebuiltCallback = false;
        
        UpdateDrawnSprite();
    }

    #region 文本所占的长宽
    //public override float preferredWidth
    //{
    //    get
    //    {
    //        var settings = GetGenerationSettings(Vector2.zero);
    //        return cachedTextGeneratorForLayout.GetPreferredWidth(_outputText, settings) / pixelsPerUnit;
    //    }
    //}
    //public override float preferredHeight
    //{
    //    get
    //    {
    //        var settings = GetGenerationSettings(new Vector2(rectTransform.rect.size.x, 0.0f));
    //        return cachedTextGeneratorForLayout.GetPreferredHeight(_outputText, settings) / pixelsPerUnit;
    //    }
    //}
	#endregion

	#region 绘制表情
    void UpdateDrawnSprite()
    {
		//记录之前的信息
		if ((_spriteInfo == null || _spriteInfo.Count == 0)&& _lastRenderIndexs.Count>0)
		{
			for (int i = 0; i < _lastRenderIndexs.Count; i++)
			{
				_inlineManager.UpdateTextInfo(this, _lastRenderIndexs[i], null);
			}
			_lastRenderIndexs.Clear();
		}
		else
		{
			_lastRenderIndexs.Clear();
			for (int i = 0; i < _spriteInfo.Count; i++)
			{
				//添加渲染id索引
				if (!_lastRenderIndexs.Contains(_spriteInfo[i].Id))
				{
					_inlineManager.UpdateTextInfo(this, _spriteInfo[i].Id,_spriteInfo.FindAll(x=>x.Id== _spriteInfo[i].Id));
					_lastRenderIndexs.Add(_spriteInfo[i].Id);
				}
			}
		}
    }

    #endregion

    #region 处理超链接的包围盒
    void CalcBoundsInfo(List<Vector3> listVertsPos, VertexHelper toFill,TextGenerationSettings settings)
    {
        #region 包围框
        // 处理超链接包围框  
        foreach (var hrefInfo in _listHrefInfos)
        {
            hrefInfo.Boxes.Clear();
            if (hrefInfo.StartIndex >= listVertsPos.Count)
            {
                continue;
            }

            // 将超链接里面的文本顶点索引坐标加入到包围框  
            var pos = listVertsPos[hrefInfo.StartIndex];
            var bounds = new Bounds(pos, Vector3.zero);
            for (int i = hrefInfo.StartIndex, m = hrefInfo.EndIndex; i < m; i++)
            {
                if (i >= listVertsPos.Count)
                {
                    break;
                }

                pos = listVertsPos[i];
                if (pos.x < bounds.min.x)
                {
					// 换行重新添加包围框  
					hrefInfo.Boxes.Add(new Rect(bounds.min, bounds.size));
                    bounds = new Bounds(pos, Vector3.zero);
                }
                else
                {
                    bounds.Encapsulate(pos); // 扩展包围框  
                }
            }
            //添加包围盒
            hrefInfo.Boxes.Add(new Rect(bounds.min, bounds.size));
        }
        #endregion

        #region 添加下划线
        TextGenerator underlineText = new TextGenerator();
        underlineText.Populate("_", settings);
        IList<UIVertex> tut = underlineText.verts;
        foreach (var item in _listHrefInfos)
        {
            for (int i = 0; i < item.Boxes.Count; i++)
            {
                //计算下划线的位置
                Vector3[] ulPos = new Vector3[4];
                ulPos[0] = item.Boxes[i].position + new Vector2(0.0f, fontSize * 0.2f);
                ulPos[1] = ulPos[0]+new Vector3(item.Boxes[i].width,0.0f);
                ulPos[2] = item.Boxes[i].position + new Vector2(item.Boxes[i].width, 0.0f);
                ulPos[3] =item.Boxes[i].position;
                //绘制下划线
                for (int j = 0; j < 4; j++)
                {
                    m_TempVerts[j] = tut[j];
                    m_TempVerts[j].color = Color.blue;
                    m_TempVerts[j].position = ulPos[j];
                    if (j == 3)
                        toFill.AddUIVertexQuad(m_TempVerts);
                }

            }
        }

        #endregion

    }
    #endregion

    #region 根据正则规则更新文本
    private string GetOutputText(string inputText)
    {
		if (string.IsNullOrEmpty(inputText))
			return "";

		//回收各种对象
        ReleaseSpriteTageInfo();
		ReleaseHrefInfos();
		_textBuilder.Remove(0, _textBuilder.Length);
        int textIndex = 0;

        foreach (Match match in _inputTagRegex.Matches(inputText))
        {
            int tempId = 0;
            if (!string.IsNullOrEmpty(match.Groups[1].Value)&& !match.Groups[1].Value.Equals("-"))
                tempId = int.Parse(match.Groups[1].Value);
            string tempTag = match.Groups[2].Value;
            //更新超链接
            if (tempId <0 )
            {
                _textBuilder.Append(inputText.Substring(textIndex, match.Index - textIndex));
                _textBuilder.Append("<color=blue>");
                int startIndex = _textBuilder.Length * 4;
                _textBuilder.Append("[" + match.Groups[2].Value + "]");
                int endIndex = _textBuilder.Length * 4 - 1;
                _textBuilder.Append("</color>");


				var hrefInfo = Pool<HrefInfo>.Get();
				hrefInfo.Id = Mathf.Abs(tempId);
				hrefInfo.StartIndex = startIndex;// 超链接里的文本起始顶点索引
				hrefInfo.EndIndex = endIndex;
				hrefInfo.Name = match.Groups[2].Value;
                _listHrefInfos.Add(hrefInfo);

            }
            //更新表情
            else
			{
                if (_inlineManager == null || !_inlineManager.IndexSpriteInfo.ContainsKey(tempId)
					|| !_inlineManager.IndexSpriteInfo[tempId].ContainsKey(tempTag))
					continue;

                SpriteInforGroup tempGroup = _inlineManager.IndexSpriteInfo[tempId][tempTag];

                _textBuilder.Append(inputText.Substring(textIndex, match.Index - textIndex));
                int tempIndex = _textBuilder.Length * 4;
                _textBuilder.Append(@"<quad size=" + tempGroup.Size + " width=" + tempGroup.Width + " />");

                //清理标签
                SpriteTagInfo tempSpriteTag = Pool<SpriteTagInfo>.Get();
				tempSpriteTag.Index = tempIndex;
				tempSpriteTag.Id = tempId;
                tempSpriteTag.Tag = tempTag;
                tempSpriteTag.Size = new Vector2(tempGroup.Size * tempGroup.Width, tempGroup.Size);
                tempSpriteTag.UVs = tempGroup.ListSpriteInfor[0].Uv;

                //添加正则表达式的信息
                _spriteInfo.Add(tempSpriteTag);
            }
            textIndex = match.Index + match.Length;
        }

        _textBuilder.Append(inputText.Substring(textIndex, inputText.Length - textIndex));
        return _textBuilder.ToString();
    }
    #endregion

    #region  超链接信息类
    public class HrefInfo
    {
        public int Id;

        public int StartIndex;

        public int EndIndex;

        public string Name;

        public readonly List<Rect> Boxes = new List<Rect>();
    }
    #endregion
    
    #region 点击事件检测是否点击到超链接文本  
    public void OnPointerClick(PointerEventData eventData)
    {
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, eventData.position, eventData.pressEventCamera, out lp);

        foreach (var hrefInfo in _listHrefInfos)
        {
            var boxes = hrefInfo.Boxes;
            for (var i = 0; i < boxes.Count; ++i)
            {
                if (boxes[i].Contains(lp))
                {
                    OnHrefClick.Invoke(hrefInfo.Name, hrefInfo.Id);
                    return;
                }
            }
        }
    }
    #endregion

	//回收SpriteTagInfo
    private void ReleaseSpriteTageInfo()
    {
        //记录之前的信息
        for (int i = 0; i < _spriteInfo.Count; i++)
        {
            //回收信息到对象池
            Pool<SpriteTagInfo>.Release(_spriteInfo[i]);
        }
        _spriteInfo.Clear();
    }
	//回收超链接的信息
	private void ReleaseHrefInfos()
	{
		for (int i = 0; i < _listHrefInfos.Count; i++)
		{
			Pool<HrefInfo>.Release(_listHrefInfos[i]);
		}
		_listHrefInfos.Clear();
	}

#if UNITY_EDITOR
	Vector3[] _textWolrdVertexs = new Vector3[4];
	private void OnDrawGizmos()
    {
		//text
        Gizmos.color = Color.white;
		rectTransform.GetWorldCorners(_textWolrdVertexs);
		Gizmos.DrawLine(_textWolrdVertexs[0], _textWolrdVertexs[1]);
		Gizmos.DrawLine(_textWolrdVertexs[1], _textWolrdVertexs[2]);
		Gizmos.DrawLine(_textWolrdVertexs[2], _textWolrdVertexs[3]);
		Gizmos.DrawLine(_textWolrdVertexs[3], _textWolrdVertexs[0]);

		//href
		Gizmos.color = Color.green;
		for (int i = 0; i < _listHrefInfos.Count; i++)
		{
			for (int j = 0; j < _listHrefInfos[i].Boxes.Count; j++)
			{
				Rect rect = _listHrefInfos[i].Boxes[j];
				Vector3 point00 = Utility.TransformPoint2World(transform,rect.position);
				Vector3 point01 = Utility.TransformPoint2World(transform, new Vector3(rect.x+rect.width,rect.y));
				Vector3 point02 = Utility.TransformPoint2World(transform, new Vector3(rect.x+rect.width,rect.y+rect.height));
				Vector3 point03 = Utility.TransformPoint2World(transform, new Vector3(rect.x,rect.y+rect.height));
				Gizmos.DrawLine(point00, point01);
				Gizmos.DrawLine(point01, point02);
				Gizmos.DrawLine(point02, point03);
				Gizmos.DrawLine(point03, point00);
			}
		}

		//sprite
		Gizmos.color = Color.yellow;
		for (int i = 0; i < _spriteInfo.Count; i++)
        {
            Gizmos.DrawLine(_spriteInfo[i].Pos[0], _spriteInfo[i].Pos[1]);
            Gizmos.DrawLine(_spriteInfo[i].Pos[1], _spriteInfo[i].Pos[2]);
            Gizmos.DrawLine(_spriteInfo[i].Pos[3], _spriteInfo[i].Pos[2]);
            Gizmos.DrawLine(_spriteInfo[i].Pos[0], _spriteInfo[i].Pos[3]);
        }
    }
#endif

}

public class SpriteTagInfo
{
	//顶点索引id
	public int Index;
    //图集id
    public int Id;
    //标签标签
    public string Tag;
    //标签大小
    public Vector2 Size;
    //表情位置
    public Vector3[] Pos=new Vector3[4];
    //uv
    public Vector2[] UVs=new Vector2[4];
}


