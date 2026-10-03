using System;
using System.Collections.Generic;
using PSD.Creator.Inspector.Utility;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector
{
#if UNITY_EDITOR
    public class Layout
    {
        public readonly List<LayoutElement> Elements = new List<LayoutElement>();

        public Layout Header(string title, string subtitle = null)
        {
            Elements.Add(new HeaderElement(title, subtitle));
            return this;
        }

        public Layout SubHeader(string title)
        {
            Elements.Add(new SubHeaderElement(title));
            return this;
        }

        public Layout Property(string property, PropertyOverride? overrides = null, Func<SerializedProperty, (bool isValid, string message, MessageType type)> validate = null)
        {
            Elements.Add(new PropertyElement(property, overrides, validate));
            return this;
        }

        public Layout Property(params (string property, PropertyOverride overrides)[] properties)
        {
            foreach (var (property, overrides) in properties)
                Elements.Add(new PropertyElement(property, overrides));
            return this;
        }

        public Layout Property(params string[] properties)
        {
            foreach (var property in properties)
                Elements.Add(new PropertyElement(property));
            return this;
        }

        public Layout BeginBox(string label = null)
        {
            Elements.Add(new BeginBoxElement(label));
            return this;
        }

        public Layout EndBox()
        {
            Elements.Add(new EndBoxElement());
            return this;
        }

        public Layout Separate()
        {
            Elements.Add(new SeparatorElement());
            return this;
        }
    }

    public abstract class LayoutElement { }

    public sealed class HeaderElement : LayoutElement
    {
        public readonly string Title;
        public readonly string Subtitle;
        public HeaderElement(string title, string subtitle) { Title = title; Subtitle = subtitle; }
    }
    public sealed class SubHeaderElement : LayoutElement
    {
        public readonly string Title;
        public SubHeaderElement(string title) => Title = title;
    }
    public sealed class PropertyElement : LayoutElement
    {
        public readonly string Property;
        public readonly PropertyOverride? Override;
        public readonly Func<SerializedProperty, (bool isValid, string message, MessageType type)> ValidationRule;

        public PropertyElement(string property, PropertyOverride? overrides = null, Func<SerializedProperty, (bool isValid, string message, MessageType type)> validate = null)
        {
            Property = property;
            Override = overrides;
            ValidationRule = validate;
        }
    }

    public sealed class BeginBoxElement : LayoutElement
    {
        public readonly string Label;
        public BeginBoxElement(string label) => Label = label;
    }

    public sealed class EndBoxElement : LayoutElement { }

    public sealed class SeparatorElement : LayoutElement { }
#endif
}