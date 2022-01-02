using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TMPro.SpriteAssetUtilities;
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UI;
using XT;

namespace UI.XT
{
    public class XTextBuildEditor : EditorWindow
    {
        [MenuItem("Assets/XEmojiBuild")]
        public static void EmojiBuild()
        {
            EditorWindow buildWindow = EditorWindow.GetWindow<XTextBuildEditor>();
            buildWindow.Show();
        }

        [MenuItem("GameObject/UI/XText")]
        public static void CreateXText()
        {
            var selectObj = Selection.activeGameObject;

            Canvas canvas = selectObj.GetComponentInParent<Canvas>();

            if (canvas == null)
            {
                GameObject go = new GameObject("Canvas", typeof(Canvas));
                selectObj = go;
            }

            Create(selectObj);
        }

        private const string defaultPath = "XText/Atlas";

        public static void Create(GameObject root)
        {
            EmojiTexPath = EditorPrefs.GetString("EmojiTexPathPref", Path.Combine(Application.dataPath, defaultPath));

            string filePath = EmojiTexPath.Remove(0, EmojiTexPath.LastIndexOf("Assets/"));

            GameObject xTextGo = new GameObject("XText", typeof(XText));
            xTextGo.transform.SetParent(root.transform, false);

            XText xTextCom = xTextGo.GetComponent<XText>();

            xTextCom.lineWidth = 1;
            TextAsset textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(Path.Combine(filePath, "EmojiTexData.txt"));
            xTextCom.texData = textAsset == null ? null : textAsset;
            xTextCom.IsRefreshData = false;
            xTextCom.text = "New XText";
            xTextCom.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 30);


            Material material = AssetDatabase.LoadAssetAtPath<Material>(Path.Combine(filePath, "EmojiTex.mat"));
            if (material != null)
                xTextCom.material = material;
            else
            {
                Debug.LogWarning("需生成material，Assets->XEmojiBuild");
            }

            Selection.activeObject = xTextGo;
        }

        const string searchPatten = "^([0-9a-zA-Z]+)(_([0-9]+))?$";

        /// <summary>
        /// 存放表情图片文件夹的路径
        /// </summary>
        string EmojiPath;

        /// <summary>
        /// 生成的表情text的路径
        /// </summary>
        static string EmojiTexPath;

        /// <summary>
        /// 单个表情的大小
        /// </summary>
        int EmojiSize = 32;

        /// <summary>
        /// 一行有多少个表情
        /// </summary>
        int LineCount = 0;

        /// <summary>
        /// 表情数据
        /// </summary>
        Dictionary<string, List<EmojiInfo>> emojiInfos = new Dictionary<string, List<EmojiInfo>>();

        StringBuilder sBuilder = new StringBuilder();

        /// <summary>
        /// 总的表情数
        /// </summary>
        public int totalFrames = 0;

        private void OnEnable()
        {
            EmojiPath = EditorPrefs.GetString("EmojiPathPref", Path.Combine(Application.dataPath, defaultPath));
            EmojiTexPath = EditorPrefs.GetString("EmojiTexPathPref", Path.Combine(Application.dataPath, defaultPath));
        }

        private void OnGUI()
        {
            GUI.skin.button.wordWrap = true;

            EditorGUILayout.BeginVertical();


            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("表情所在文件夹：", EmojiPath);
            if (GUILayout.Button("选择路径", GUILayout.Width(0)))
            {
                EmojiPath = EditorUtility.OpenFolderPanel("选择表情所在的文件夹", EmojiPath, EmojiPath);
                EditorPrefs.SetString("EmojiPathPref", EmojiPath);
            }

            EditorGUILayout.EndHorizontal();


            EmojiSize = EditorGUILayout.IntField("表情大小(2的整数幂)：", EmojiSize);


            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField("图集生成路径：", EmojiTexPath);
            if (GUILayout.Button("选择路径", GUILayout.Width(0)))
            {
                EmojiTexPath = EditorUtility.OpenFolderPanel("图集生成路径", EmojiTexPath, EmojiTexPath);
                EditorPrefs.SetString("EmojiTexPathPref", EmojiTexPath);
            }

            EditorGUILayout.EndHorizontal();


            if (GUILayout.Button("生成表情图集", GUILayout.Width(0)))
            {
                OnCreateBtnClick();
            }


            EditorGUILayout.EndVertical();
        }


        public void OnCreateBtnClick()
        {
            emojiInfos = new Dictionary<string, List<EmojiInfo>>();
            totalFrames = 0;
            sBuilder.Clear();
            string[] filePath = Directory.GetFiles(EmojiPath, "*.png");

            for (int i = 0; i < filePath.Length; i++)
            {
                string path = filePath[i].Remove(0, filePath[i].LastIndexOf("Assets/"));

                Texture2D emojiTex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Match match = Regex.Match(emojiTex.name, searchPatten);

                if (match.Success && match.Groups.Count > 0 && emojiTex.isReadable)
                {
                    EmojiInfo emojiInfo = new EmojiInfo()
                    {
                        name = match.Groups[0].Value,
                        self = emojiTex
                    };

                    if (emojiInfos.ContainsKey(match.Groups[1].Value))
                    {
                        emojiInfos[match.Groups[1].Value].Add(emojiInfo);
                    }
                    else
                    {
                        emojiInfos.Add(match.Groups[1].Value, new List<EmojiInfo>() {emojiInfo});
                    }

                    totalFrames++;
                }
                else
                {
                    Debug.LogError($"{emojiTex.name}   名字不匹配或者不可读");
                }

                EmojiSize = emojiTex.width > EmojiSize ? emojiTex.width : EmojiSize;
            }

            CreateAlta();
            Debug.Log("---------生成表情图集成功！！！-----------");
        }


        public void CreateAlta()
        {
            sBuilder.AppendLine("name\ttotalFrame\tindx");

            int altaSize = ComputeAltaSize();
            if (altaSize <= 0)
            {
                Debug.Log("没有找到合适的图集大小，已经达到上限2048");
                return;
            }

            Texture2D alta = new Texture2D(altaSize, altaSize, TextureFormat.RGBA32, false);


            int xStart = 0;
            int yStart = 0;

            int idx = 0;
            foreach (var value in emojiInfos)
            {
                for (int i = 0; i < value.Value.Count; i++)
                {
                    Texture2D emojiTex = value.Value[i].self;

                    Color[] _colors = emojiTex.GetPixels(0, 0, EmojiSize, EmojiSize);

                    yStart = xStart >= altaSize ? yStart + EmojiSize : yStart;
                    xStart = xStart >= altaSize ? 0 : xStart;

                    alta.SetPixels(xStart, yStart, EmojiSize, EmojiSize, _colors);


                    xStart += EmojiSize;
                }

                sBuilder.AppendLine($"{value.Key}\t{value.Value.Count}\t{idx}");

                idx += value.Value.Count;
            }

            byte[] altaData = alta.EncodeToPNG();

            string texPath = Path.Combine(EmojiTexPath, "EmojiTex.png");
            string dataPath = Path.Combine(EmojiTexPath, "EmojiTexData.txt");

            File.WriteAllBytes(texPath, altaData);

            File.WriteAllText(dataPath, sBuilder.ToString());

            CreateMaterial();
            AssetDatabase.Refresh();
        }

        public void CreateMaterial()
        {
            string filePath = EmojiTexPath.Remove(0, EmojiTexPath.LastIndexOf("Assets/"));
            Material material = new Material(Shader.Find("UI/EmojiFont"));
            Texture2D emojiTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Path.Combine(filePath, "EmojiTex.png"));
            material.SetTexture("_EmojiTex", emojiTex);
            material.SetFloat("_EmojiSize", 1.0f / LineCount);
            material.SetFloat("_LineCount", LineCount);
            AssetDatabase.CreateAsset(material, Path.Combine(filePath, "EmojiTex.mat"));
        }

        public int ComputeAltaSize()
        {
            GetEmojiWrapSize();
            //最大一张表情图片2048*2048
            for (int i = 0; i < 13; i++)
            {
                int w = 1 << i;
                if (w * w >= totalFrames * EmojiSize * EmojiSize)
                {
                    LineCount = (int) (w / EmojiSize);

                    return w;
                }
            }

            return 0;
        }


        public void GetEmojiWrapSize()
        {
            //最大一张表情图片256*256
            for (int i = 0; i < 8; i++)
            {
                if (2 << i >= EmojiSize)
                {
                    EmojiSize = 2 << i;
                    break;
                }
            }
        }
    }


    public struct EmojiInfo
    {
        public Texture2D self;
        public string name;
    }
}