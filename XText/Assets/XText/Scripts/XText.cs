using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace XT
 {
    public class XText : Text, IPointerClickHandler
    {
        public bool IsRefreshData;
        public TextAsset texData;

        private bool isDirty;

        /// <summary>
        /// 表情text里面的数据
        /// </summary>
        Dictionary<string, EmojiInfoText> EmojiInfos;

        /// <summary>
        /// 在文字中匹配到的表情数据，key 是文本被替换后的网格索引，比如：[E1]333，则第一个就是的key就是0
        /// </summary>
        Dictionary<int, EmojiInfoData> EmojiInfoDatas;

        /// <summary>
        /// key是第几个网格
        /// </summary>
        Dictionary<int, HyperLinkInfo> HyperLinksDataDic;

        Dictionary<int, HyperLinkInfo> m_tempHyperLinksDataDic;

        /// <summary>
        /// 网格值
        /// </summary>
        private Dictionary<int, int> HyperIdxs;


        MatchResultData matchResult = new MatchResultData();

        const string regexPatten =
            "\\[(u%|d%)?(E[0-9]+|Link)?(#[a-fA-F0-9]{6}#)?([\u4e00-\u9fa5_a-zA-Z0-9]+)?\\]";


        StringBuilder sBuilder = new StringBuilder();
        UIVertex[] m_TempVerts = new UIVertex[4];

        /// <summary>
        /// 下划线的宽度
        /// </summary>
        public int lineWidth = 1;


        /// <summary>
        /// 匹配字符的总长度
        /// </summary>
        int tempMatchValueLength = 0;

        private string m_outText = "";

        /// <summary>
        /// 文本
        /// </summary>
        public override string text
        {
            get => m_Text;

            set
            {
                if (EmojiInfos == null || IsRefreshData)
                    InitEmojiData();
                m_tempHyperLinksDataDic = new Dictionary<int, HyperLinkInfo>();
                ParseText(value);
                base.text = value;

                //Debug.Log("---设置文字内容-----");

                isDirty = true;
            }
        }

        public override float preferredWidth
        {
            get
            {
                if (m_outText != m_Text)
                    m_outText = m_Text;
                var settings = GetGenerationSettings(Vector2.zero);
                return cachedTextGeneratorForLayout.GetPreferredWidth(m_outText, settings) / pixelsPerUnit;
            }
        }

        public override float preferredHeight
        {
            get
            {
                if (m_outText != m_Text)
                    m_outText = m_Text;
                var settings = GetGenerationSettings(new Vector2(rectTransform.rect.size.x, 0.0f));
                return cachedTextGeneratorForLayout.GetPreferredHeight(m_outText, settings) / pixelsPerUnit;
            }
        }


        protected void InitEmojiData()
        {
            if (texData != null)
            {
                EmojiInfos = new Dictionary<string, EmojiInfoText>();
                string[] lines = texData.text.Split('\n');

                for (int i = 1; i < lines.Length - 1; i++)
                {
                    string[] data = lines[i].Split('\t');
                    EmojiInfoText emojiInfo = new EmojiInfoText()
                    {
                        name = data[0],
                        totalFrame = int.Parse(data[1]),
                        idx = int.Parse(data[2])
                    };

                    EmojiInfos[emojiInfo.name] = emojiInfo;
                }
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position,
                eventData.pressEventCamera, out Vector2 localPos))
            {
                if (HyperLinksDataDic != null)
                {
                    foreach (var links in HyperLinksDataDic)
                    {
                        for (int i = 0; i < links.Value.clickArea.Count; i++)
                        {
                            if (links.Value.clickArea[i].Contains(localPos))
                            {
                                if (links.Value.ClickFunc != null)
                                    links.Value.ClickFunc(links.Value.customInfo);
                            }
                        }
                    }
                }
            }
        }


        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            //Debug.Log("刷新网格");
            if (font == null)
                return;
            if (EmojiInfos == null || IsRefreshData)
                InitEmojiData();

            ParseText(m_Text);

            // We don't care if we the font Texture changes while we are doing our Update.
            // The end result of cachedTextGenerator will be valid for this instance.
            // Otherwise we can get issues like Case 619238.
            m_DisableFontTextureRebuiltCallback = true;

            Vector2 extents = rectTransform.rect.size;

            var settings = GetGenerationSettings(extents);
            //cachedTextGenerator.PopulateWithErrors(m_Text, settings, gameObject);
            cachedTextGenerator.PopulateWithErrors(m_outText, settings, gameObject);

            //Debug.Log("----" + m_outText);

            // Apply the offset to the vertices
            IList<UIVertex> verts = cachedTextGenerator.verts;
            float unitsPerPixel = 1 / pixelsPerUnit;
            int vertCount = verts.Count;

            // We have no verts to process just return (case 1037923)
            if (vertCount <= 0)
            {
                toFill.Clear();
                return;
            }

            Vector2 roundingOffset = new Vector2(verts[0].position.x, verts[0].position.y) * unitsPerPixel;
            roundingOffset = PixelAdjustPoint(roundingOffset) - roundingOffset;
            toFill.Clear();
            if (roundingOffset != Vector2.zero)
            {
                for (int i = 0; i < vertCount; ++i)
                {
                    int tempVertsIndex = i & 3;
                    m_TempVerts[tempVertsIndex] = verts[i];
                    m_TempVerts[tempVertsIndex].position *= unitsPerPixel;
                    m_TempVerts[tempVertsIndex].position.x += roundingOffset.x;
                    m_TempVerts[tempVertsIndex].position.y += roundingOffset.y;
                    if (tempVertsIndex == 3)
                        toFill.AddUIVertexQuad(m_TempVerts);
                }
            }
            else
            {
                int quadIdx = 0;
                for (int i = 0; i < vertCount; ++i)
                {
                    quadIdx = i / 4;
                    int tempVertsIndex = i & 3;

                    m_TempVerts[tempVertsIndex] = verts[i];
                    if (EmojiInfoDatas != null && EmojiInfoDatas.TryGetValue(quadIdx, out EmojiInfoData data))
                    {
                        //Debug.Log("表情的顶点开始索引：" + i);
                        m_TempVerts[tempVertsIndex].uv0.x += data.emojiInfoText.idx * 10;

                        m_TempVerts[tempVertsIndex].uv0.y += data.emojiInfoText.totalFrame * 10;
                    }
                    else if (HyperLinksDataDic != null &&
                             HyperLinksDataDic.TryGetValue(quadIdx, out HyperLinkInfo hyperData))
                    {
                        if (hyperData.clickArea.Count <= 0)
                        {
                            CalculateClickRect(i, hyperData, verts);
                            SetVertsColor(i, toFill, hyperData, verts);
                            i += hyperData.wordLength * 4 - 1;
                            //Debug.Log("连接的顶点结束索引：" + i);
                        }
                    }

                    m_TempVerts[tempVertsIndex].position *= unitsPerPixel;
                    if (tempVertsIndex == 3)
                        toFill.AddUIVertexQuad(m_TempVerts);
                }

                DrawLine(toFill, m_TempVerts);
            }

            if (isDirty)
            {
                RefreshData();
                isDirty = false;
            }

            m_DisableFontTextureRebuiltCallback = false;
        }


        protected void ParseText(string inputText)
        {
            m_outText = inputText;

            // if (!Application.isPlaying)
            //     return;

            EmojiInfoDatas = new Dictionary<int, EmojiInfoData>();
            HyperLinksDataDic = new Dictionary<int, HyperLinkInfo>();
            HyperIdxs = new Dictionary<int, int>();
            tempMatchValueLength = 0;


            MatchCollection matches = Regex.Matches(m_outText, regexPatten);

            for (int i = 0; i < matches.Count; i++)
            {
                sBuilder.Clear();
                if (matches[i].Success && matches[i].Groups.Count > 0)
                {
                    var match = matches[i];

                    matchResult.Reset();
                    matchResult.ParseData(match);

                    if (matchResult.matchType == MatchType.Emoji)
                    {
                        if (EmojiInfos != null)
                            AnalyseEmojiData(match);
                        else
                        {
                            Debug.LogWarning("----没有生成表情数据-----");
                            continue;
                        }
                    }
                    else if (matchResult.matchType == MatchType.Link || matchResult.matchType == MatchType.Line)
                    {
                        AnalyseLinkData(match);
                    }
                    else
                    {
                    }
                }
            }
        }

        protected void AnalyseEmojiData(Match match)
        {
            sBuilder.Append($"<quad size={fontSize} width=1 />");

            m_outText = m_outText.Replace(match.Groups[0].Value, sBuilder.ToString());

            if (!EmojiInfos.ContainsKey(match.Groups[2].Value))
            {
                Debug.LogError("表情与txt的不匹配，检查表情拼写，或者重新生成表情数据");
            }
            else
            {
                EmojiInfoDatas[match.Index - tempMatchValueLength] = new EmojiInfoData()
                {
                    emojiInfoText = EmojiInfos[match.Groups[2].Value]
                };

                //Debug.Log("表情的网格-----" + (match.Index - tempMatchValueLength));
                tempMatchValueLength += match.Groups[0].Length - 1;
            }
        }


        protected void AnalyseLinkData(Match match)
        {
            HyperLinkInfo hyperLinkInfo = new HyperLinkInfo();
            hyperLinkInfo.color = color;

            if (!string.IsNullOrEmpty(matchResult.hyperColor))
                if (ColorUtility.TryParseHtmlString(matchResult.hyperColor, out hyperLinkInfo.color))
                    tempMatchValueLength += match.Groups[3].Length;

            if (matchResult.matchType == MatchType.Link)
            {
                tempMatchValueLength += match.Groups[2].Length + 1;

                hyperLinkInfo.ClickFunc = s => Debug.Log("点击到了" + s);
                hyperLinkInfo.lineType = LineType.None;
                hyperLinkInfo.startIdx = match.Groups[4].Index - tempMatchValueLength;


                // Debug.Log("Link---是第几个网格--" + hyperLinkInfo.startIdx);

                tempMatchValueLength++;
            }
            else if (matchResult.matchType == MatchType.Line)
            {
                tempMatchValueLength += match.Groups[1].Length + 1;
                hyperLinkInfo.ClickFunc = s => Debug.Log("点击到了" + s);
                hyperLinkInfo.lineType = match.Groups[1].Value == "u%" ? LineType.Underline : LineType.Delete;
                hyperLinkInfo.lineArea = new List<Rect>();
                hyperLinkInfo.startIdx = match.Groups[4].Index - tempMatchValueLength;


                // Debug.Log("下划线开始网格:" + hyperLinkInfo.startIdx + "  结束网格:" +
                //           (hyperLinkInfo.startIdx + match.Groups[4].Length));

                tempMatchValueLength++;
            }

            //m_Text = m_Text.Replace(match.Groups[0].Value, match.Groups[4].Value);
            m_outText = m_outText.Replace(match.Groups[0].Value, match.Groups[4].Value);

            hyperLinkInfo.id = HyperLinksDataDic.Count + 1;
            hyperLinkInfo.wordLength = match.Groups[4].Length;
            hyperLinkInfo.clickArea = new List<Rect>();
            hyperLinkInfo.word = match.Groups[4].Value;
            hyperLinkInfo.customInfo = hyperLinkInfo.word;


            HyperLinksDataDic[hyperLinkInfo.startIdx] = hyperLinkInfo;
            HyperIdxs[HyperLinksDataDic.Count] = hyperLinkInfo.startIdx;
        }

        protected void DrawLine(VertexHelper toFill, UIVertex[] m_TempVerts)
        {
            if (HyperLinksDataDic != null)
            {
                var verts = cachedTextGenerator.verts;
                foreach (var lineInfo in HyperLinksDataDic)
                {
                    if (lineInfo.Value.lineType == LineType.None)
                        continue;
                    for (int i = 0; i < lineInfo.Value.lineArea.Count; i++)
                    {
                        Rect rect = lineInfo.Value.lineArea[i];
                        for (int j = 0; j < 4; j++)
                        {
                            m_TempVerts[j] = verts[lineInfo.Value.startIdx * 4 + j];
                            m_TempVerts[j].uv0 = -10f * Vector4.one;
                            m_TempVerts[j].color = lineInfo.Value.color;
                            m_TempVerts[0].position =
                                j == 0 ? new Vector3(rect.xMin, rect.yMax) : m_TempVerts[0].position;
                            m_TempVerts[1].position =
                                j == 1 ? new Vector3(rect.xMax, rect.yMax) : m_TempVerts[1].position;
                            m_TempVerts[2].position =
                                j == 2 ? new Vector3(rect.xMax, rect.yMin) : m_TempVerts[2].position;
                            m_TempVerts[3].position =
                                j == 3 ? new Vector3(rect.xMin, rect.yMin) : m_TempVerts[3].position;

                            if (j == 3)
                                toFill.AddUIVertexQuad(m_TempVerts);
                        }
                    }
                }
            }
        }

        protected void CalculateClickRect(int vertsIdx, HyperLinkInfo hyperLinkInfo, IList<UIVertex> verts)
        {
            Vector2 minPos = verts[vertsIdx + 3].position;
            Vector2 maxPos = verts[vertsIdx + 1].position;
            while (vertsIdx / 4 < hyperLinkInfo.startIdx + hyperLinkInfo.wordLength)
            {
                Vector2 vertsPos = verts[vertsIdx + 1].position;

                if (vertsPos.x > maxPos.x)
                    maxPos.x = vertsPos.x;
                if (vertsPos.y > maxPos.y)
                    maxPos.y = vertsPos.y;
                if (vertsPos.y < minPos.y)
                {
                    // Debug.Log("--------换行了-----------------");
                    CalculateClickRect(vertsIdx, hyperLinkInfo, verts);
                    break;
                }


                vertsIdx += 4;
            }

            hyperLinkInfo.clickArea.Add(new Rect {min = minPos, max = maxPos});

            if (hyperLinkInfo.lineType == LineType.Underline)
            {
                minPos = new Vector2(minPos.x, minPos.y - lineWidth);
                maxPos = new Vector2(maxPos.x, minPos.y + lineWidth);
            }
            else if (hyperLinkInfo.lineType == LineType.Delete)
            {
                minPos = new Vector2(minPos.x, minPos.y + (maxPos.y - minPos.y) * 0.5f - lineWidth * 0.5f);
                maxPos = new Vector2(maxPos.x, minPos.y + lineWidth);
            }

            if (hyperLinkInfo.lineType != LineType.None)
                hyperLinkInfo.lineArea.Add(new Rect {min = minPos, max = maxPos});
        }

        protected void SetVertsColor(int vertIdx, VertexHelper toFill, HyperLinkInfo hyperLinkInfo,
            IList<UIVertex> verts)
        {
            int startVertIdx = vertIdx;
            float unitsPerPixel = 1 / pixelsPerUnit;

            while (vertIdx < startVertIdx + hyperLinkInfo.wordLength * 4)
            {
                int tempVertsIndex = vertIdx & 3;
                m_TempVerts[tempVertsIndex] = verts[vertIdx];

                m_TempVerts[tempVertsIndex].color = hyperLinkInfo.color;

                m_TempVerts[tempVertsIndex].position *= unitsPerPixel;
                if (tempVertsIndex == 3)
                    toFill.AddUIVertexQuad(m_TempVerts);

                vertIdx++;
            }
        }

        /*---------可点击区域----------*/

        public void SetHyperData(int id, Action<string> func,
            string customInfo,
            string lineType = "0", string hyperColor = "#FFFFFF")
        {
            HyperLinkInfo hyperLinkInfo = GetLinkInfoByIdx(id);
            if (hyperLinkInfo != null)
            {
                hyperLinkInfo.ClickFunc = func;
                hyperLinkInfo.customInfo = customInfo;
                hyperLinkInfo.lineType = (LineType) Enum.Parse(typeof(LineType), lineType);
                ColorUtility.TryParseHtmlString(hyperColor, out hyperLinkInfo.color);

                m_tempHyperLinksDataDic[HyperIdxs[id]] = hyperLinkInfo;

                Debug.Log("-----自定义成功！！！------");
            }
        }

        private void RefreshData()
        {
            foreach (var hyperInfo in m_tempHyperLinksDataDic)
            {
                HyperLinksDataDic[hyperInfo.Key].color = hyperInfo.Value.color;
                HyperLinksDataDic[hyperInfo.Key].customInfo = hyperInfo.Value.customInfo;
                HyperLinksDataDic[hyperInfo.Key].ClickFunc = hyperInfo.Value.ClickFunc;
                HyperLinksDataDic[hyperInfo.Key].lineType = hyperInfo.Value.lineType;
            }
        }

        private int GetLinkMeshKeyByIdx(int idx)
        {
            if (HyperIdxs == null || !HyperIdxs.ContainsKey(idx))
                return -1;
            else
                return HyperIdxs[idx];
        }

        private HyperLinkInfo GetLinkInfoByIdx(int idx)
        {
            int meshKey = GetLinkMeshKeyByIdx(idx);
            if (meshKey != -1)
            {
                if (HyperLinksDataDic == null || !HyperLinksDataDic.ContainsKey(meshKey))
                    return null;
                else
                    return HyperLinksDataDic[HyperIdxs[idx]];
            }
            else
                return null;
        }
    }

    [Serializable]
    public struct EmojiInfoText
    {
        public string name; //该图片的名字
        public int totalFrame; //该图片的总共帧数
        public int idx; //该图片的索引，有可能不是连续的
    }

    public struct EmojiInfoData
    {
        public EmojiInfoText emojiInfoText;
    }


    public class HyperLinkInfo
    {
        public LineType lineType;
        public int id;
        public string customInfo;
        public int startIdx;
        public int wordLength;
        public string word;
        public Action<string> ClickFunc;
        public Color color;
        public List<Rect> clickArea; //防止换行的情况
        public List<Rect> lineArea;
    }


    public class MatchResultData
    {
        public string prefix;
        public MatchType matchType;
        public string hyperColor;

        public void Reset()
        {
            prefix = string.Empty;
            matchType = MatchType.None;
            hyperColor = null;
        }

        public void ParseData(Match match)
        {
            if (match.Groups.Count == 5)
            {
                prefix = match.Groups[2].Value;
                if (!string.IsNullOrEmpty(prefix))
                {
                    if (prefix == "Link")
                    {
                        if (!string.IsNullOrEmpty(match.Groups[3].Value))
                        {
                            matchType = MatchType.Link;
                        }
                    }
                    else
                    {
                        matchType = MatchType.Emoji;
                    }
                }
                else
                    matchType = MatchType.None;

                if (!string.IsNullOrEmpty(match.Groups[1].Value) && string.IsNullOrEmpty(match.Groups[2].Value) &&
                    !string.IsNullOrEmpty(match.Groups[4].Value))
                    matchType = MatchType.Line;

                if (!string.IsNullOrEmpty(match.Groups[3].Value))
                {
                    hyperColor = match.Groups[3].Value.Substring(0, 7);
                }
            }
        }
    }

    public enum MatchType
    {
        None,
        Emoji,
        Link,
        Line
    }

    public enum LineType
    {
        None,
        Delete,
        Underline
    } 
 }