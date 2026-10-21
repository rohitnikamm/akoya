using System.Windows;
using Akoya.App.Controls;
using Akoya.Core.Ui;

namespace Akoya.App.Setup;

/// <summary>
/// Attached properties typed <c>object</c> that forward to array- and list-typed properties, which the XAML compiler
/// can't set inside templates (MC4102).
/// </summary>
public static class TemplateBindings
{
    /// <summary>Forwards a <c>double[]</c> to <see cref="DotGrid.Opacities"/>.</summary>
    public static readonly DependencyProperty DotOpacitiesProperty = DependencyProperty.RegisterAttached(
        "DotOpacities", typeof(object), typeof(TemplateBindings),
        new PropertyMetadata(null, (d, e) =>
        {
            if (d is DotGrid grid) grid.Opacities = e.NewValue as double[];
        }));

    /// <summary>Forwards a box-shadow recipe to <see cref="DotGrid.Halo"/> or <see cref="RimGlowBorder.Shadows"/>.</summary>
    public static readonly DependencyProperty ShadowsProperty = DependencyProperty.RegisterAttached(
        "Shadows", typeof(object), typeof(TemplateBindings),
        new PropertyMetadata(null, (d, e) =>
        {
            var recipe = e.NewValue as IReadOnlyList<BoxShadow>;
            if (d is DotGrid grid) grid.Halo = recipe;
            else if (d is RimGlowBorder border) border.Shadows = recipe;
        }));

    public static object? GetDotOpacities(DependencyObject element) => element?.GetValue(DotOpacitiesProperty);

    public static void SetDotOpacities(DependencyObject element, object? value) => element?.SetValue(DotOpacitiesProperty, value);

    public static object? GetShadows(DependencyObject element) => element?.GetValue(ShadowsProperty);

    public static void SetShadows(DependencyObject element, object? value) => element?.SetValue(ShadowsProperty, value);
}
