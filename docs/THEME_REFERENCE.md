# BRIECH UAS Theme - Quick Reference Guide

## Using the Professional Theme in Your Code

### Access Theme Colors Anywhere
```csharp
// Use the theme constants in any file
using MissionPlanner;

// Apply to a control
myButton.BackColor = BriechUASTheme.DarkNavy;
myButton.ForeColor = BriechUASTheme.LightGray;
myLabel.ForeColor = BriechUASTheme.DimGray;

// Status indicators
statusPanel.BackColor = BriechUASTheme.StatusGreen;  // Good
statusPanel.BackColor = BriechUASTheme.StatusYellow; // Warning
statusPanel.BackColor = BriechUASTheme.StatusRed;    // Error
```

### Color Reference
```
Primary Colors:
  DarkNavy      #1a1f2e  (26, 31, 46)   - Main background
  Charcoal      #282d3c  (40, 45, 60)   - Panel backgrounds
  GoldAccent    #c9a961  (201, 169, 97) - Button highlights
  
Text Colors:
  LightGray     #dcdcdc  (220, 220, 220) - Primary text
  DimGray       #969696  (150, 150, 150) - Secondary text
  BorderGold    #b49650  (180, 150, 80)  - Borders
  
Status Colors:
  StatusGreen   #4caf50  (76, 175, 80)   - Active/Good
  StatusYellow  #ffc107  (255, 193, 7)   - Warning
  StatusRed     #f44336  (244, 67, 54)   - Error
```

### Apply Theme to New Form
```csharp
// In your new form's constructor
public MyCustomForm() : Form
{
    InitializeComponent();
    
    // Apply BRIECH UAS theme
    this.BackColor = BriechUASTheme.DarkNavy;
    this.ForeColor = BriechUASTheme.LightGray;
    
    // Style buttons
    foreach (Button btn in this.GetAllButtons())
    {
        btn.BackColor = BriechUASTheme.DarkNavy;
        btn.ForeColor = BriechUASTheme.LightGray;
        btn.FlatStyle = FlatStyle.Flat;
        btn.FlatAppearance.BorderColor = BriechUASTheme.GoldAccent;
    }
}
```

### Style Telemetry Panels
```csharp
// For status indicators in telemetry view
public void UpdateConnectionStatus(bool connected)
{
    if (connected)
    {
        statusLight.BackColor = BriechUASTheme.StatusGreen;
        statusLabel.Text = "CONNECTED";
    }
    else
    {
        statusLight.BackColor = BriechUASTheme.StatusRed;
        statusLabel.Text = "DISCONNECTED";
    }
}

// For warning indicators
public void UpdateBatteryStatus(float voltage)
{
    if (voltage > 12.5f)
        batteryLight.BackColor = BriechUASTheme.StatusGreen;
    else if (voltage > 11.0f)
        batteryLight.BackColor = BriechUASTheme.StatusYellow;
    else
        batteryLight.BackColor = BriechUASTheme.StatusRed;
}
```

### Theme Application Methods

#### For Entire Form
```csharp
// Called automatically in MainV2.cs
ApplyBriechUASTheme();
```

#### For Specific Control Collection
```csharp
// Can be called from other forms
MainV2.instance.StyleControlsForTheme(
    myPanel.Controls,
    BriechUASTheme.DarkNavy,
    BriechUASTheme.Charcoal,
    BriechUASTheme.LightGray,
    BriechUASTheme.DimGray,
    BriechUASTheme.GoldAccent
);
```

#### For ToolStrip
```csharp
// Apply custom renderer to any toolstrip
var renderer = new ProfessionalToolStripRenderer(
    BriechUASTheme.DarkNavy,
    BriechUASTheme.GoldAccent
);
myToolStrip.Renderer = renderer;
```

## Design Guidelines

### Button Styling
- Background: `DarkNavy`
- Text: `LightGray`
- Border: `GoldAccent` (1px, flat style)
- Hover: Gold background with light text
- Pressed: Slightly darker gold

### Text Styling
- Primary labels: `LightGray`
- Secondary labels: `DimGray`
- Disabled text: `DimGray` with reduced opacity
- Links/Interactive: `GoldAccent`

### Panel/Container Styling
- Default panels: `Charcoal` background
- Container background: `DarkNavy`
- Borders: `BorderGold` 1px stroke

### Status Indicators
- Active/Connected: `StatusGreen`
- Warning/Low: `StatusYellow`
- Error/Disconnected: `StatusRed`
- Inactive: `DimGray`

## Example: Complete Telemetry Panel

```csharp
public class TelemetryPanel : Panel
{
    public TelemetryPanel()
    {
        // Main panel
        this.BackColor = BriechUASTheme.Charcoal;
        this.ForeColor = BriechUASTheme.LightGray;
        this.BorderStyle = BorderStyle.FixedSingle;
        
        // Title label
        var titleLabel = new Label()
        {
            Text = "TELEMETRY",
            ForeColor = BriechUASTheme.GoldAccent,
            BackColor = Color.Transparent,
            Font = new Font("Arial", 10, FontStyle.Bold)
        };
        
        // Value label
        var valueLabel = new Label()
        {
            Text = "---",
            ForeColor = BriechUASTheme.LightGray,
            BackColor = Color.Transparent,
            Font = new Font("Courier New", 12, FontStyle.Bold)
        };
        
        // Status indicator
        var statusIndicator = new Panel()
        {
            Size = new Size(12, 12),
            BackColor = BriechUASTheme.StatusGreen,
            BorderStyle = BorderStyle.None
        };
        
        this.Controls.Add(titleLabel);
        this.Controls.Add(valueLabel);
        this.Controls.Add(statusIndicator);
    }
}
```

## Maintenance Tips

### Updating Colors
1. Edit `BriechUASTheme` class in `MainV2.cs`
2. Update the `Color.FromArgb()` values
3. Colors will automatically apply everywhere they're referenced

### Adding New Status Colors
```csharp
public static class BriechUASTheme
{
    // Add new status color
    public static readonly Color StatusOrange = Color.FromArgb(255, 152, 0);
}
```

### Creating Theme Variants
```csharp
// Could extend for light theme in future
public static class BriechUASThemeDark // Current
public static class BriechUASThemeLight // Future
public static class BriechUASThemeHighContrast // Future
```

## Troubleshooting

### Colors Not Applying
- Ensure control inherits from standard WinForms controls
- Some custom controls may need manual styling
- Check for `BackColor = Color.Transparent` override

### Text Not Visible
- Verify text color has sufficient contrast (7:1 ratio minimum)
- Check for background color conflicts
- Ensure ForeColor is set after BackColor

### Hover/Focus States Not Showing
- Apply `FlatStyle = FlatStyle.Flat` to buttons
- Set `FlatAppearance.BorderColor` to `GoldAccent`
- Configure `MouseDown/MouseUp/MouseEnter/MouseLeave` events

## Support
For questions about the theme, refer to `UI_REDESIGN_SUMMARY.md` for complete documentation.
