using System;
using System.Reflection;
using UnityEditor.Experimental.UIElements;
using UnityEngine;
using UnityEngine.Experimental.UIElements;
using UnityEngine.Experimental.UIElements.StyleSheets;
using UnityEditor.ShaderGraph;

namespace UnityEditor.ShaderGraph.Drawing.Controls
{
    [AttributeUsage(AttributeTargets.Property)]
    public class GradientControlAttribute : Attribute, IControlAttribute
    {
        string m_Label;

        public GradientControlAttribute(string label = null)
        {
            m_Label = label;
        }

        public VisualElement InstantiateControl(AbstractMaterialNode node, PropertyInfo propertyInfo)
        {
            return new GradientControlView(m_Label, node, propertyInfo);
        }
    }

    public class GradientControlView : VisualElement
    {
        AbstractMaterialNode m_Node;
        PropertyInfo m_PropertyInfo;

        Gradient m_Gradient;
        GradientField m_GradientField;

        public GradientControlView(string label, AbstractMaterialNode node, PropertyInfo propertyInfo)
        {
            m_Node = node;
            m_PropertyInfo = propertyInfo;
            if (propertyInfo.PropertyType != typeof(Gradient))
                throw new ArgumentException("Property must be of type Gradient.", "propertyInfo");
            label = label ?? ObjectNames.NicifyVariableName(propertyInfo.Name);

            m_Gradient = (Gradient)m_PropertyInfo.GetValue(m_Node, null);

            if (!string.IsNullOrEmpty(label))
                Add(new Label(label));

            m_GradientField = new GradientField { value = m_Gradient };
            m_GradientField.OnValueChanged(OnChange);
            Add(m_GradientField);
        }

        void OnChange(ChangeEvent<Gradient> evt)
        {
            m_Node.owner.owner.RegisterCompleteObjectUndo("Gradient Change");
            m_Gradient = evt.newValue;
            m_PropertyInfo.SetValue(m_Node, m_Gradient, null);
            Dirty(ChangeType.Repaint);
        }
    }
}
