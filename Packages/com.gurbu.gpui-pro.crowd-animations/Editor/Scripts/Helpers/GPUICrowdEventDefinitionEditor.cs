// GPU Instancer Pro
// Copyright (c) GurBu Technologies

using System;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Events;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.UIElements;

namespace GPUInstancerPro.CrowdAnimations
{
    [CustomEditor(typeof(GPUICrowdEventDefinition))]
    public class GPUICrowdEventDefinitionEditor : GPUIEditor
    {
        private GPUICrowdEventDefinition _eventDefinition;
        private VisualElement _root;

        protected override void OnEnable()
        {
            base.OnEnable();

            _eventDefinition = (GPUICrowdEventDefinition)target;
            _eventDefinition.clipEventDefinitions ??= new();
        }

        public override void DrawContentGUI(VisualElement contentElement)
        {
            _root = contentElement;
            contentElement.Clear();

            contentElement.Add(DrawSerializedProperty(serializedObject.FindProperty("isDontDestroyOnLoad")));
            contentElement.Add(DrawSerializedProperty(serializedObject.FindProperty("crowdPrefab")));

            SerializedProperty clipEventDefinitionsSP = serializedObject.FindProperty("clipEventDefinitions");
            for (int i = 0; i < clipEventDefinitionsSP.arraySize; i++)
            {
                DrawClipEventDefinition(contentElement, clipEventDefinitionsSP.GetArrayElementAtIndex(i), i);
            }

            Button addClipButton = GPUIEditorUtility.CreateStyledButton("Add Animation Clip", GPUIEditorConstants.Colors.green);
            addClipButton.clicked += OnAddClip;
            addClipButton.style.marginTop = 10;
            contentElement.Add(addClipButton);
        }

        private void OnAddClip()
        {
            serializedObject.ApplyModifiedProperties();
            _eventDefinition.clipEventDefinitions.Add(new GPUICrowdClipEventDefinition());
            serializedObject.Update();
            DrawContentGUI(_root);
        }

        private void OnRemoveClip(int definitionIndex)
        {
            if (!EditorUtility.DisplayDialog("Remove Animation Clip", "Do you wish to remove all the events for this Animation Clip?", "Yes", "No"))
                return;
            serializedObject.ApplyModifiedProperties();
            _eventDefinition.clipEventDefinitions.RemoveAt(definitionIndex);
            serializedObject.Update();
            DrawContentGUI(_root);
        }

        private void DrawClipEventDefinition(VisualElement container, SerializedProperty property, int definitionIndex)
        {
            VisualElement definitionContainer = new VisualElement();
            container.Add(definitionContainer);
            
            Foldout eventDefinitionFoldout = GPUIEditorUtility.DrawBoxContainer(definitionContainer, "Clip " + definitionIndex, false);
            
            VisualElement animationClipVE = DrawSerializedProperty(property.FindPropertyRelative("animationClip"), "", out _);
            animationClipVE.style.position = Position.Absolute;
            animationClipVE.style.top = 10;
            animationClipVE.style.left = 70;
            animationClipVE.style.width = 170;
            definitionContainer.Add(animationClipVE);

            Button removeButton = GPUIEditorUtility.CreateStyledButton("X", GPUIEditorConstants.Colors.darkRed);
            removeButton.clicked += () => OnRemoveClip(definitionIndex);
            removeButton.style.position = Position.Absolute;
            removeButton.style.top = 10;
            removeButton.style.right = 5;
            removeButton.style.width = 20;
            removeButton.focusable = false;
            definitionContainer.Add(removeButton);

            DrawEventDefinitionFoldoutContent(eventDefinitionFoldout, property, definitionIndex);
        }

        private void DrawEventDefinitionFoldoutContent(Foldout eventDefinitionFoldout, SerializedProperty property, int definitionIndex)
        {
            eventDefinitionFoldout.Clear();

            SerializedProperty animationEventsSP = property.FindPropertyRelative("animationEvents");
            for (int i = 0; i < animationEventsSP.arraySize; i++)
            {
                DrawAnimationEvent(eventDefinitionFoldout, animationEventsSP.GetArrayElementAtIndex(i), definitionIndex, i);
            }
            Button addEventButton = GPUIEditorUtility.CreateStyledButton("Add Event", GPUIEditorConstants.Colors.darkBlue);
            addEventButton.clicked += () => OnAddEvent(eventDefinitionFoldout, property, definitionIndex);
            addEventButton.style.marginTop = 10;
            eventDefinitionFoldout.Add(addEventButton);
        }

        private void OnAddEvent(Foldout eventDefinitionFoldout, SerializedProperty property, int definitionIndex)
        {
            serializedObject.ApplyModifiedProperties();
            _eventDefinition.clipEventDefinitions[definitionIndex].animationEvents ??= new();
            _eventDefinition.clipEventDefinitions[definitionIndex].animationEvents.Add(new GPUICrowdAnimationEvent());
            serializedObject.Update();
            DrawEventDefinitionFoldoutContent(eventDefinitionFoldout, property, definitionIndex);
        }

        private void DrawAnimationEvent(Foldout eventDefinitionFoldout, SerializedProperty property, int definitionIndex, int eventIndex)
        {
            VisualElement eventContainer = new VisualElement();
            //eventContainer.AddToClassList("gpui-border");
            //eventContainer.AddToClassList("gpui-bg-light");
            eventContainer.style.marginTop = 5;
            eventDefinitionFoldout.Add(eventContainer);

            Foldout eventFoldout = GPUIEditorUtility.DrawBoxContainer(eventContainer, "Event " + eventIndex + " - Time:", false);

            VisualElement normalizedTriggerTimeVE = DrawSerializedProperty(property.FindPropertyRelative("normalizedTriggerTime"), "", out _);
            normalizedTriggerTimeVE.style.position = Position.Absolute;
            normalizedTriggerTimeVE.style.top = 10;
            normalizedTriggerTimeVE.style.left = 120;
            normalizedTriggerTimeVE.style.right = 80;
            GPUIEditorUtility.RegisterHideLabelEvent(normalizedTriggerTimeVE);
            eventContainer.Add(normalizedTriggerTimeVE);

            Button removeButton = GPUIEditorUtility.CreateStyledButton("X", GPUIEditorConstants.Colors.darkRed);
            removeButton.clicked += () => OnRemoveEvent(eventDefinitionFoldout, definitionIndex, eventIndex);
            removeButton.style.position = Position.Absolute;
            removeButton.style.top = 10;
            removeButton.style.right = 5;
            removeButton.style.width = 20;
            removeButton.focusable = false;
            eventContainer.Add(removeButton);

            SerializedProperty parametersSP = property.FindPropertyRelative("parameters");
            Foldout paramsFoldout = new Foldout();
            paramsFoldout.style.marginTop = 5;
            paramsFoldout.text = "Parameters";
            paramsFoldout.value = false;
            paramsFoldout.Add(DrawSerializedProperty(parametersSP.FindPropertyRelative("floatParam")));
            paramsFoldout.Add(DrawSerializedProperty(parametersSP.FindPropertyRelative("intParam")));
            paramsFoldout.Add(DrawSerializedProperty(parametersSP.FindPropertyRelative("stringParam")));
            eventFoldout.Add(paramsFoldout);
            eventFoldout.Add(DrawSerializedProperty(property));
        }

        private void OnRemoveEvent(Foldout eventDefinitionFoldout, int definitionIndex, int eventIndex)
        {
            if (!EditorUtility.DisplayDialog("Remove Event", "Do you wish to remove the event?", "Yes", "No"))
                return;
            serializedObject.ApplyModifiedProperties();
            _eventDefinition.clipEventDefinitions[definitionIndex].animationEvents.RemoveAt(eventIndex);
            serializedObject.Update();
            DrawEventDefinitionFoldoutContent(eventDefinitionFoldout, serializedObject.FindProperty("clipEventDefinitions").GetArrayElementAtIndex(definitionIndex), definitionIndex);
        }

        public override string GetTitleText() => "GPUI Crowd Event Definition";
        public override string GetVersionNoText() => GPUICrowdEditorConstants.GetVersionNoText();
        public override string GetWikiURLParams() => "title=GPU_Instancer_Pro-Crowd_Animations#GPUI_Crowd_Event_Definition";
    }
}