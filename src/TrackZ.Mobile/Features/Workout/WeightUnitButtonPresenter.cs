namespace TrackZ.Mobile.Features.Workout;

public static class WeightUnitButtonPresenter
{
    public static void Apply(
        Button kilogramsButton,
        Button poundsButton,
        WeightDisplayUnit selectedUnit)
    {
        ArgumentNullException.ThrowIfNull(kilogramsButton);
        ArgumentNullException.ThrowIfNull(poundsButton);
        if (!Enum.IsDefined(selectedUnit))
            throw new ArgumentOutOfRangeException(nameof(selectedUnit));

        ApplySelection(kilogramsButton, selectedUnit == WeightDisplayUnit.Kilograms);
        ApplySelection(poundsButton, selectedUnit == WeightDisplayUnit.Pounds);
    }

    private static void ApplySelection(Button button, bool selected)
    {
        button.SetDynamicResource(
            Button.BackgroundColorProperty,
            selected ? "TrackZPrimary" : "TrackZSurfaceRaised");
        button.SetDynamicResource(
            Button.TextColorProperty,
            selected ? "TrackZPrimaryContrast" : "TrackZTextPrimary");
    }
}
