using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using Wpf.Ui.Converters;

namespace ZTMZ.PacenoteTool.Base.UI;

public partial class CommonSettingsItem : UserControl
{
    public CommonSettingsItem()
    {
        // InitializeComponent();
    }

        /// <summary>
    /// Property for <see cref="Icon"/>.
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(IconElement),
        typeof(CommonSettingsItem),
        new PropertyMetadata(null, null, IconSourceElementConverter.ConvertToIconElement)
    );

        /// <summary>
    /// Gets or sets displayed <see cref="IconElement"/>.
    /// </summary>
    public IconElement? Icon
    {
        get => (IconElement)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
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

    // [Bindable(true), TypeConverter(typeof(I18NToStringConverter))]
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

    // [Bindable(true), TypeConverter(typeof(I18NToStringConverter))]
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

    // [Bindable(true)]
    public bool? SettingsPropertyBooleanValue
    {
        get
        {
            // Debug.WriteLine("SettingsPropertyBooleanValue: " + (bool)GetValue(SettingsPropertyBooleanValueProperty));
            return (bool)GetValue(SettingsPropertyBooleanValueProperty);
        }
        set
        {
            // Debug.WriteLine("SettingsPropertyBooleanValue: " + value);
            SetValue(SettingsPropertyBooleanValueProperty, value);
        }
    }

    public static DependencyProperty SettingsPropertyBooleanValueProperty = DependencyProperty.Register(
        nameof(SettingsPropertyBooleanValue),
        typeof(bool),
        typeof(CommonSettingsItem),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuePropertyChangedCallback));

    [Bindable(true)]
    public string SettingsPropertyName
    {
        get
        {
            // Debug.WriteLine("SettingsPropertyName: " + (string)GetValue(SettingsPropertyNameProperty));
            return (string)GetValue(SettingsPropertyNameProperty);
        }
        set
        {
            // Debug.WriteLine("SettingsPropertyName: " + value);
            SetValue(SettingsPropertyNameProperty, value);
            var prop = typeof(Config).GetProperty(value);
            if (prop != null) {
                if (prop.PropertyType == typeof(bool)) {
                    SettingsPropertyBooleanValue = (bool)prop.GetValue(Config.Instance);
                }
            }
        }
    }

    public static DependencyProperty SettingsPropertyNameProperty = DependencyProperty.Register(
        nameof(SettingsPropertyName),
        typeof(string),
        typeof(CommonSettingsItem),
        new PropertyMetadata(""));
    private static void OnValuePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e) {
        Debug.WriteLine("OnValuePropertyChangedCallback: " + e.NewValue);
        var self = (CommonSettingsItem)d;
        var configProperty = typeof(Config).GetProperty(self.SettingsPropertyName);
        if (configProperty != null) {
            configProperty.SetValue(Config.Instance, e.NewValue);
            Config.Instance.SaveUserConfig();
        }
    }
}

