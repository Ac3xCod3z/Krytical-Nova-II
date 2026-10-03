using UnityEngine;
using System;
using System.Reflection;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.CameraShake.Utility
{

	[AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
	public class ShowIfAttribute : PropertyAttribute
	{
		public string variable;
		public Enum[] enumValues;

		public ShowIfAttribute(string variable, params object[] enumValues)
		{
			this.variable = variable;
			this.enumValues = Array.ConvertAll(enumValues, v => v as Enum);

			if (this.enumValues.Length == 0)
				throw new ArgumentNullException(nameof(enumValues), "This parameter must be an enum value.");
		}

		public ShowIfAttribute(string variable)
		{
			this.variable = variable;
			enumValues = null;
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(ShowIfAttribute))]
	public class ShowIfPropertyDrawer : PropertyDrawer
	{
		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return ShouldShow(property) ? EditorGUI.GetPropertyHeight(property, label, true) : 0;
		}

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (ShouldShow(property))
			{
				EditorGUI.PropertyField(position, property, label, true);
			}
		}


		private bool ShouldShow(SerializedProperty property)
		{
			var attribute = this.attribute as ShowIfAttribute;

			var target = PropertyUtility.GetNestedTargetObject(property);
			if (target == null) return true;

			FieldInfo info = fieldInfo.DeclaringType.GetField(attribute.variable, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

			if (info == null) return true;

			object variable = info.GetValue(target);

			if (variable is Array arrayValue && info.FieldType.GetElementType().IsEnum)
			{
				foreach (Enum _enumValue in arrayValue)
				{
					if (attribute.enumValues.Any(e => e.Equals(_enumValue)))
						return true;
				}
				return false;
			}
			if (attribute.enumValues != null && variable is Enum enumValue)
				return attribute.enumValues.Any(e => e.Equals(enumValue));
			if (variable is bool boolValue)
				return boolValue;

			return true;
		}
	}
#endif

}