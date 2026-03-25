# BRIECH Integration Test Diagnostic

## Pre-Test Verification Script

Run through these verification steps BEFORE testing:

### **Step 1: Verify All Required Files Exist**

Check that all these files exist in your project:

```
Required Files - BRIECH Components:
✓ GCSViews/ModernFlightDataAdapter.cs
✓ GCSViews/ModernFlightDataComplete.cs  
✓ GCSViews/FlightDataViewController.cs
✓ GCSViews/TopNavigationBar.cs
✓ GCSViews/BriechStatusBar.cs
✓ GCSViews/BriechTheme.cs
✓ GCSViews/BriechTypes.cs
✓ GCSViews/BriechEventArgs.cs
✓ GCSViews/TelemetrySimulator.cs

If any file is MISSING:
→ They were created in previous steps
→ Check your GCSViews folder
→ Files should be in: ProjectRoot/GCSViews/
```

### **Step 2: Verify Compilation**

Open Visual Studio and verify:

```
1. Build > Clean Solution
2. Build > Rebuild Solution
3. Expected: ✅ Build succeeded, 0 errors, 0 warnings

If you see errors:
- Hover over red squigglies
- Read the error message carefully
- Common issues:
  * Missing "using" statements
  * MyUserControl not found (namespace issue)
  * BriechTheme colors undefined
  * Circular dependencies
```

### **Step 3: Verify Code Structure**

In Visual Studio, check **Solution Explorer**:

```
MissionPlanner
├── GCSViews
│   ├── ModernFlightDataAdapter.cs      ← Should show in Solution Explorer
│   ├── ModernFlightDataComplete.cs     ← Should show in Solution Explorer
│   ├── FlightDataViewController.cs     ← Should show in Solution Explorer
│   ├── TopNavigationBar.cs             ← Should show in Solution Explorer
│   ├── BriechStatusBar.cs              ← Should show in Solution Explorer
│   ├── BriechTheme.cs                  ← Should show in Solution Explorer
│   ├── BriechTypes.cs                  ← Should show in Solution Explorer
│   ├── BriechEventArgs.cs              ← Should show in Solution Explorer
│   ├── TelemetrySimulator.cs           ← Should show in Solution Explorer
│   └── FlightData.cs                   ← Existing file (reference)
├── MainV2.cs                           ← MODIFIED for integration
└── [Other files...]

All BRIECH files should be in GCSViews folder
All should appear in Solution Explorer with blue file icons (no red "!")
```

### **Step 4: Verify MainV2.cs Integration Points**

Open **MainV2.cs** and search for these lines (Ctrl+F):

```
Line 595: public GCSViews.ModernFlightDataAdapter ModernFlightData;
  → Should exist as field declaration
  → Type should be ModernFlightDataAdapter (not ModernFlightDataComplete)

Line ~795: ModernFlightData = new GCSViews.ModernFlightDataAdapter();
  → Should exist in constructor
  → Should be inside try-catch block
  → Should have logging statements

Line ~1631: MenuModernFlightData_Click handler
  → Should call MainV2.I.ModernFlightData or similar
  → Should navigate to the screen

Search for: MenuModernFlightData
  → Should find button creation code
  → Button text should be "Modern Flight"
  → Button should be added to MainMenu.Items
```

### **Step 5: Check Dependencies**

Verify that these **using statements** are in the BRIECH files:

**ModernFlightDataAdapter.cs should have:**
```csharp
using System;
using System.Windows.Forms;
using log4net;
using System.Reflection;
```

**ModernFlightDataComplete.cs should have:**
```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using log4net;
```

**FlightDataViewController.cs should have:**
```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
```

**All should inherit from MyUserControl:**
```csharp
public partial class ModernFlightDataAdapter : MyUserControl { }
public partial class ModernFlightDataComplete : MyUserControl { }
public class FlightDataViewController : MyUserControl { }
public class TopNavigationBar : MyUserControl { }
```

### **Step 6: Verify Inheritance Chain**

Check that inheritance is correct:

```
MyUserControl
├── ModernFlightDataAdapter (NEW - integration adapter)
│   └── Contains: ModernFlightDataComplete
├── FlightDataViewController (NEW - HUD controller)
└── TopNavigationBar (NEW - navigation)

These are the ONLY new components that inherit from MyUserControl.

Verify by:
1. Open each file
2. Find the class declaration line
3. Should see: public class ClassName : MyUserControl
4. Should NOT see: public class ClassName : UserControl
5. Should NOT see: public class ClassName : Control
```

### **Step 7: Verify Log4Net Configuration**

BRIECH components use log4net. Verify:

```
1. log4net should be added to references
   → Right-click project > References
   → Should see "log4net" in the list
   
2. If missing:
   → Install via NuGet: Install-Package log4net -Version 2.0.14
   → (Or check MainV2 project for log4net version being used)
```

---

## **Ready to Test?**

If all verifications above are ✅ **PASSED**:

→ Proceed to **QUICK_TEST_CHECKLIST.md** for 5-minute smoke test
→ Then proceed to **TESTING_GUIDE_BRIECH_INTEGRATION.md** for detailed testing

---

## **Diagnostic Checklist**

Copy and use this while verifying:

```
PRE-TEST VERIFICATION CHECKLIST

File Existence:
☐ ModernFlightDataAdapter.cs exists
☐ ModernFlightDataComplete.cs exists
☐ FlightDataViewController.cs exists
☐ TopNavigationBar.cs exists
☐ BriechStatusBar.cs exists
☐ BriechTheme.cs exists
☐ BriechTypes.cs exists
☐ BriechEventArgs.cs exists
☐ TelemetrySimulator.cs exists

Compilation:
☐ Solution builds with 0 errors
☐ Solution builds with 0 warnings
☐ No red squigglies in code
☐ All file icons are blue (no red !)

Inheritance:
☐ ModernFlightDataAdapter : MyUserControl
☐ ModernFlightDataComplete : MyUserControl
☐ FlightDataViewController : MyUserControl
☐ TopNavigationBar : MyUserControl

Integration Points:
☐ Line 595: Field declaration exists
☐ Line ~795: Constructor initialization exists
☐ MenuModernFlightData button creation exists
☐ Screen registration code exists

Dependencies:
☐ log4net reference exists
☐ System.Drawing.dll referenced
☐ System.Windows.Forms.dll referenced
☐ All using statements present

VERIFICATION COMPLETE: ☐ ALL PASS ☐ SOME FAIL

If ALL PASS → Ready for QUICK_TEST_CHECKLIST.md
If ANY FAIL → Review this document and fix issues
```

---

## **Troubleshooting Verification Failures**

### If File Missing Error:
```
Solution:
1. Check GCSViews folder in Windows Explorer
2. File should be there
3. If not, check git status: git status
4. If deleted, restore: git checkout GCSViews/filename.cs
5. Rebuild solution
```

### If Compilation Error:
```
Solution:
1. Read error message in Error List
2. Double-click error → goes to problem line
3. Check inheritance at top of file
4. Check using statements
5. If "MyUserControl not found":
   → Add: using System.Windows.Forms;
   → Check ExtLibs/Controls/MyUserControl.cs exists
6. Rebuild solution
```

### If Integration Points Missing:
```
Solution:
1. Search MainV2.cs for "ModernFlightDataAdapter"
2. If 0 results → Integration may not be complete
3. Check previous integration steps
4. May need to manually add field and initialization
5. Refer to INTEGRATION_GUIDE_BRIECH.md
```

---

**Ready? Start with verification checklist above, then proceed to testing!**

