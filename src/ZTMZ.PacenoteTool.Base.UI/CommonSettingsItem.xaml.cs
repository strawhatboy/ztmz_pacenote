using System;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;
using Wpf.Ui.Converters;

namespace ZTMZ.PacenoteTool.Base.UI;

public partial class CommonSettingsItem : UserControl
{
    public CommonSettingsItem() {
        InitializeComponent();
        this.DataContext = this;

        // add databinding to property value
        if (DataType == typeof(bool)) {
            var toggleSwitch = new ToggleSwitch();
            this.the_content.Content = toggleSwitch;
            toggleSwitch.SetBinding(ToggleSwitch.IsCheckedProperty, new Binding(nameof(SettingsPropertyValue)) {
                Source = this,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        } else if (DataType == typeof(string)) {
            var textBox = new Wpf.Ui.Controls.TextBox();
            this.the_content.Content = textBox;
            textBox.SetBinding(Wpf.Ui.Controls.TextBox.TextProperty, new Binding(nameof(SettingsPropertyValue)) {
                Source = this,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        } else if (DataType == typeof(int)) {
            var intInput = new Wpf.Ui.Controls.NumberBox();
            this.the_content.Content = intInput;
            intInput.SetBinding(Wpf.Ui.Controls.NumberBox.ValueProperty, new Binding(nameof(SettingsPropertyValue)) {
                Source = this,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        }
    }

        /// <summary>
    /// Property for <see cref="Icon"/>.
    /// </summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(IconElement),
        typeof(CommonSettingsItem),
        new PropertyMetadata(null, null, null)
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
    public object? SettingsPropertyValue
    {
        get
        {
            // Debug.WriteLine("SettingsPropertyBooleanValue: " + (bool)GetValue(SettingsPropertyBooleanValueProperty));
            return GetValue(SettingsPropertyValueProperty);
        }
        set
        {
            // Debug.WriteLine("SettingsPropertyBooleanValue: " + value);
            SetValue(SettingsPropertyValueProperty, value);
        }
    }

    public static DependencyProperty SettingsPropertyValueProperty = DependencyProperty.Register(
        nameof(SettingsPropertyValue),
        typeof(object),
        typeof(CommonSettingsItem),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValuePropertyChangedCallback));

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
        }
    }

    public static DependencyProperty SettingsPropertyNameProperty = DependencyProperty.Register(
        nameof(SettingsPropertyName),
        typeof(string),
        typeof(CommonSettingsItem),
        new PropertyMetadata("", OnSettingsPropertyNamePropertyChangedCallback));

    private static void OnSettingsPropertyNamePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // set property value
        var prop = typeof(Config).GetProperty(e.NewValue.ToString());
        if (prop != null) {
            if (prop.PropertyType == typeof(bool)) {
                d.SetValue(SettingsPropertyValueProperty, (bool)prop.GetValue(Config.Instance));
            }
        }
    }

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

