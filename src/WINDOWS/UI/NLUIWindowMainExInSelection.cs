using NOVORA.ExInEngine;
using NOVORA.Model;
using System.Windows;
using System.Windows.Controls;

namespace NOVORA;

/// <summary>ExIn page selectors: target phone, followed controller and forwarding scope.</summary>
public partial class NLUIWindowMain
{
    private sealed record ExInPhoneOption(string? Serial, string Label);

    private string? _exInManualSerialVE;
    private bool _exInSelectionApplyingVE;

    private void ApplyExInSelectionUiVE(bool enabled, bool calibrating)
    {
        _exInSelectionApplyingVE = true;
        try
        {
            ApplyExInPhoneOptionsVE(enabled);
            ApplyExInControllerOptionsVE(enabled && !calibrating);
            ExInForwardSelectedOnlyCheck14.IsChecked = _exInEngine?.Manager.ForwardSelectedOnlyVE == true;
            ExInForwardSelectedOnlyCheck14.IsEnabled = enabled && _exInEngine is not null;
        }
        finally
        {
            _exInSelectionApplyingVE = false;
        }
    }

    private void ApplyExInPhoneOptionsVE(bool enabled)
    {
        var options = new List<ExInPhoneOption> { new(null, "Automático") };
        foreach (NLModelDeviceInfo device in _viewModel.Devices)
        {
            if (!device.Connected || string.IsNullOrWhiteSpace(device.Serial)) continue;
            // An ADB Wi-Fi serial is host:port; the label never shows the serial or IP.
            string route = device.Serial.Contains(':') ? "ADB Wi-Fi" : "USB";
            options.Add(new(device.Serial, $"{device.FriendlyName} · {route}"));
        }
        if (_exInManualSerialVE is { } manual &&
            !options.Any(option => string.Equals(option.Serial, manual, StringComparison.OrdinalIgnoreCase)))
            options.Add(new(manual, "Teléfono elegido · no conectado"));

        if (!ExInPhoneComboBox14.Items.Cast<ExInPhoneOption>().SequenceEqual(options))
            ExInPhoneComboBox14.ItemsSource = options;
        ExInPhoneComboBox14.SelectedItem = options.FirstOrDefault(option =>
            string.Equals(option.Serial, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        ExInPhoneComboBox14.IsEnabled = enabled && options.Count > 1;

        string? active = _exInControlSession?.Serial;
        ExInPhoneText14.Text = _exInManualSerialVE is null
            ? "Automático: USB primero, después ADB Wi-Fi."
            : active is not null && string.Equals(active, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)
                ? "ExIn envía al teléfono elegido."
                : "ExIn se está conectando al teléfono elegido.";
    }

    private void ApplyExInControllerOptionsVE(bool enabled)
    {
        IReadOnlyList<ExInControllerChoice> controllers =
            _exInEngine?.Manager.ControllersVE ?? Array.Empty<ExInControllerChoice>();
        if (!ExInControllerComboBox14.Items.Cast<ExInControllerChoice>().SequenceEqual(controllers))
            ExInControllerComboBox14.ItemsSource = controllers;
        uint? selected = _exInEngine?.Manager.SelectedControllerVE;
        ExInControllerComboBox14.SelectedItem = controllers.FirstOrDefault(choice => choice.InstanceId == selected);
        ExInControllerComboBox14.IsEnabled = enabled && controllers.Count > 1;
    }

    private void ExInPhone_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_exInSelectionApplyingVE || ExInPhoneComboBox14.SelectedItem is not ExInPhoneOption option) return;
        if (string.Equals(option.Serial, _exInManualSerialVE, StringComparison.OrdinalIgnoreCase)) return;
        _exInManualSerialVE = option.Serial;
        ShowTopMessage14(option.Serial is null
            ? "ExIn elegirá el teléfono automáticamente."
            : $"ExIn enviará el control a {option.Label}.", NLUIMessageKind14.Success);
        _ = EnsureExInStandaloneAsync();
    }

    private void ExInController_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_exInSelectionApplyingVE || _exInEngine is null ||
            ExInControllerComboBox14.SelectedItem is not ExInControllerChoice choice) return;
        if (_exInEngine.Manager.SelectControllerVE(choice.InstanceId))
        {
            _androidControlRevision++;
            ApplyGamepadShell14(_exInEngine.Status);
        }
    }

    private void ExInForwardSelectedOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_exInSelectionApplyingVE || _exInEngine is null) return;
        _exInEngine.Manager.ForwardSelectedOnlyVE = ExInForwardSelectedOnlyCheck14.IsChecked == true;
    }
}
