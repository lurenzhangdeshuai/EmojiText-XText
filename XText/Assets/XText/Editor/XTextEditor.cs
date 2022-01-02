using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using XT;
using TextEditor = UnityEditor.UI.TextEditor;


    [CustomEditor(typeof(XText), true)]
    [CanEditMultipleObjects]
    public class XTextEditor : TextEditor
    {
        public SerializedProperty texData;
        public SerializedProperty IsRefreshData;
        public SerializedProperty LineWidth;

        protected override void OnEnable()
        {
            base.OnEnable();
            texData = serializedObject.FindProperty("texData");
            IsRefreshData = serializedObject.FindProperty("IsRefreshData");
            LineWidth = serializedObject.FindProperty("lineWidth");
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            //serializedObject.Update();
            EditorGUILayout.ObjectField(texData);

            if (texData.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("没有设置表情数据", MessageType.Warning);
            }

            IsRefreshData.boolValue = EditorGUILayout.ToggleLeft("是否刷新数据", IsRefreshData.boolValue);
            LineWidth.intValue = EditorGUILayout.IntField("下划线宽度", LineWidth.intValue);

            serializedObject.ApplyModifiedProperties();
        }
    }
