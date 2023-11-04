using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace ZTMZ.PacenoteTool.Base.UI;

public partial class CommonSettingsItem : UserControl
{
    public CommonSettingsItem()
    {
        // InitializeComponent();
    }

    [Bindable(true), TypeConverter(typeof(StringToTypeConverter))]
    public Type DataType
    {
        get
        {
            return (Type)GetValue(DataTypeProperty);
        }
        set
        {
            SetValue(DataTypeProperty, value);
        }
    }

    public static DependencyProperty DataTypeProperty = DependencyProperty.Register(
        nameof(DataType),
        typeof(Type),
        typeof(CommonSettingsItem),
        new PropertyMetadata(typeof(bool)));

    [Bindable(true), TypeConverter(typeof(I18NToStringConverter))]
    public string Label
    {
        get
        {
            return (string)GetValue(LabelProperty);
        }
        set
        {
            SetValue(LabelProperty, value);
        }
    }

    public static DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label),
        typeof(string),
        typeof(CommonSettingsItem),
        new PropertyMetadata(""));

    [Bindable(true), TypeConverter(typeof(I18NToStringConverter))]
    public string Description
    {
        get
        {
            return (string)GetValue(DescriptionProperty);
        }
        set
        {
            SetValue(DescriptionProperty, value);
        }
    }

    public static DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(CommonSettingsItem),
        new PropertyMetadata(""));

    [Bindable(true)]
    public object Value
    {
        get
        {
            return (object)GetValue(ValueProperty);
        }
        set
        {
            SetValue(ValueProperty, value);
        }
    }

    public static DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(object),
        typeof(CommonSettingsItem),
        new PropertyMetadata(null, OnValuePropertyChangedCallback));

    [Bindable(true)]
    public string SettingsPropertyName
    {
        get
        {
            return (string)GetValue(SettingsPropertyNameProperty);
        }
        set
        {
            SetValue(SettingsPropertyNameProperty, value);
            Value = typeof(Config).GetProperty(value)?.GetValue(Config.Instance);
        }
    }

    public static DependencyProperty SettingsPropertyNameProperty = DependencyProperty.Register(
        nameof(SettingsPropertyName),
        typeof(string),
        typeof(CommonSettingsItem),
        new PropertyMetadata(""));
    private static void OnValuePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        var self = (CommonSettingsItem)d;
        var configProperty = typeof(Config).GetProperty(self.SettingsPropertyName);
        if (configProperty != null) {
            configProperty.SetValue(Config.Instance, e.NewValue);
            Config.Instance.SaveUserConfig();
        }
    }
}

