
// #define DEV
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Xps.Serialization;
using GameOverlay.Windows;
using GameOverlay.Drawing;
using SharpDX;
using SharpDX.Direct2D1;
using ZTMZ.PacenoteTool.Base;
using Geometry = GameOverlay.Drawing.Geometry;
using Image = GameOverlay.Drawing.Image;
using ZTMZ.PacenoteTool.Base.Game;

namespace ZTMZ.PacenoteTool
{
    public class SystemMethods
    {
        public enum WinEventFlags : uint
        {
            WINEVENT_OUTOFCONTEXT = 0x0000, // Events are ASYNC
            WINEVENT_SKIPOWNTHREAD = 0x0001, // Don't call back for events on installer's thread
            WINEVENT_SKIPOWNPROCESS = 0x0002, // Don't call back for events on installer's process
            WINEVENT_INCONTEXT = 0x0004, // Events are SYNC, this causes your dll to be injected into every process
        }

        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
            IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        public enum WinEvents : uint
        {
            /** The range of WinEvent constant values specified by the Accessibility Interoperability Alliance (AIA) for use across the industry.
* For more information, see Allocation of WinEvent IDs. */
            EVENT_AIA_START = 0xA000,
            EVENT_AIA_END = 0xAFFF,

            /** The lowest and highest possible event values.
*/
            EVENT_MIN = 0x00000001,
            EVENT_MAX = 0x7FFFFFFF,

            /** An object's KeyboardShortcut property has changed. Server applications send this event for their accessible objects.
*/
            EVENT_OBJECT_ACCELERATORCHANGE = 0x8012,

            /** Sent when a window is cloaked. A cloaked window still exists, but is invisible to the user.
*/
            EVENT_OBJECT_CLOAKED = 0x8017,

            /** A window object's scrolling has ended. Unlike EVENT_SYSTEM_SCROLLEND, this event is associated with the scrolling window.
* Whether the scrolling is horizontal or vertical scrolling, this event should be sent whenever the scroll action is completed. * The hwnd parameter of the WinEventProc callback function describes the scrolling window; the idObject parameter is OBJID_CLIENT, * and the idChild parameter is CHILDID_SELF. */
            EVENT_OBJECT_CONTENTSCROLLED = 0x8015,

            /** An object has been created. The system sends this event for the following user interface elements: caret, header control,
* list-view control, tab control, toolbar control, tree view control, and window object. Server applications send this event * for their accessible objects. * Before sending the event for the parent object, servers must send it for all of an object's child objects. * Servers must ensure that all child objects are fully created and ready to accept IAccessible calls from clients before * the parent object sends this event. * Because a parent object is created after its child objects, clients must make sure that an object's parent has been created * before calling IAccessible::get_accParent, particularly if in-context hook functions are used. */
            EVENT_OBJECT_CREATE = 0x8000,

            /** An object's DefaultAction property has changed. The system sends this event for dialog boxes. Server applications send
* this event for their accessible objects. */
            EVENT_OBJECT_DEFACTIONCHANGE = 0x8011,

            /** An object's Description property has changed. Server applications send this event for their accessible objects.
*/
            EVENT_OBJECT_DESCRIPTIONCHANGE = 0x800D,

            /** An object has been destroyed. The system sends this event for the following user interface elements: caret, header control,
* list-view control, tab control, toolbar control, tree view control, and window object. Server applications send this event for * their accessible objects. * Clients assume that all of an object's children are destroyed when the parent object sends this event. * After receiving this event, clients do not call an object's IAccessible properties or methods. However, the interface pointer * must remain valid as long as there is a reference count on it (due to COM rules), but the UI element may no longer be present. * Further calls on the interface pointer may return failure errors; to prevent this, servers create proxy objects and monitor * their life spans. */
            EVENT_OBJECT_DESTROY = 0x8001,

            /** The user started to drag an element. The hwnd, idObject, and idChild parameters of the WinEventProc callback function
* identify the object being dragged. */
            EVENT_OBJECT_DRAGSTART = 0x8021,

            /** The user has ended a drag operation before dropping the dragged element on a drop target. The hwnd, idObject, and idChild
* parameters of the WinEventProc callback function identify the object being dragged. */
            EVENT_OBJECT_DRAGCANCEL = 0x8022,

            /** The user dropped an element on a drop target. The hwnd, idObject, and idChild parameters of the WinEventProc callback
* function identify the object being dragged. */
            EVENT_OBJECT_DRAGCOMPLETE = 0x8023,

            /** The user dragged an element into a drop target's boundary. The hwnd, idObject, and idChild parameters of the WinEventProc
* callback function identify the drop target. */
            EVENT_OBJECT_DRAGENTER = 0x8024,

            /** The user dragged an element out of a drop target's boundary. The hwnd, idObject, and idChild parameters of the WinEventProc
* callback function identify the drop target. */
            EVENT_OBJECT_DRAGLEAVE = 0x8025,

            /** The user dropped an element on a drop target. The hwnd, idObject, and idChild parameters of the WinEventProc callback
* function identify the drop target. */
            EVENT_OBJECT_DRAGDROPPED = 0x8026,

            /** The highest object event value.
*/
            EVENT_OBJECT_END = 0x80FF,

            /** An object has received the keyboard focus. The system sends this event for the following user interface elements:
* list-view control, menu bar, pop-up menu, switch window, tab control, tree view control, and window object. * Server applications send this event for their accessible objects. * The hwnd parameter of the WinEventProc callback function identifies the window that receives the keyboard focus. */
            EVENT_OBJECT_FOCUS = 0x8005,

            /** An object's Help property has changed. Server applications send this event for their accessible objects.
*/
            EVENT_OBJECT_HELPCHANGE = 0x8010,

            /** An object is hidden. The system sends this event for the following user interface elements: caret and cursor.
* Server applications send this event for their accessible objects. * When this event is generated for a parent object, all child objects are already hidden. * Server applications do not send this event for the child objects. * Hidden objects include the STATE_SYSTEM_INVISIBLE flag; shown objects do not include this flag. The EVENT_OBJECT_HIDE event * also indicates that the STATE_SYSTEM_INVISIBLE flag is set. Therefore, servers do not send the EVENT_STATE_CHANGE event in * this case. */
            EVENT_OBJECT_HIDE = 0x8003,

            /** A window that hosts other accessible objects has changed the hosted objects. A client might need to query the host
* window to discover the new hosted objects, especially if the client has been monitoring events from the window. * A hosted object is an object from an accessibility framework (MSAA or UI Automation) that is different from that of the host. * Changes in hosted objects that are from the same framework as the host should be handed with the structural change events, * such as EVENT_OBJECT_CREATE for MSAA. For more info see comments within winuser.h. */
            EVENT_OBJECT_HOSTEDOBJECTSINVALIDATED = 0x8020,

            /** An IME window has become hidden.
*/
            EVENT_OBJECT_IME_HIDE = 0x8028,

            /** An IME window has become visible.
*/
            EVENT_OBJECT_IME_SHOW = 0x8027,

            /** The size or position of an IME window has changed.
*/
            EVENT_OBJECT_IME_CHANGE = 0x8029,

            /** An object has been invoked; for example, the user has clicked a button. This event is supported by common controls and is
* used by UI Automation. * For this event, the hwnd, ID, and idChild parameters of the WinEventProc callback function identify the item that is invoked. */
            EVENT_OBJECT_INVOKED = 0x8013,

            /** An object that is part of a live region has changed. A live region is an area of an application that changes frequently
* and/or asynchronously. */
            EVENT_OBJECT_LIVEREGIONCHANGED = 0x8019,

            /** An object has changed location, shape, or size. The system sends this event for the following user interface elements:
* caret and window objects. Server applications send this event for their accessible objects. * This event is generated in response to a change in the top-level object within the object hierarchy; it is not generated for any * children that the object might have. For example, if the user resizes a window, the system sends this notification for the window, * but not for the menu bar, title bar, scroll bar, or other objects that have also changed. * The system does not send this event for every non-floating child window when the parent moves. However, if an application explicitly * resizes child windows as a result of resizing the parent window, the system sends multiple events for the resized children. * If an object's State property is set to STATE_SYSTEM_FLOATING, the server sends EVENT_OBJECT_LOCATIONCHANGE whenever the object changes * location. If an object does not have this state, servers only trigger this event when the object moves in relation to its parent. * For this event notification, the idChild parameter of the WinEventProc callback function identifies the child object that has changed. */
            EVENT_OBJECT_LOCATIONCHANGE = 0x800B,

            /** An object's Name property has changed. The system sends this event for the following user interface elements: check box,
* cursor, list-view control, push button, radio button, status bar control, tree view control, and window object. Server * * applications send this event for their accessible objects. */
            EVENT_OBJECT_NAMECHANGE = 0x800C,

            /** An object has a new parent object. Server applications send this event for their accessible objects.
*/
            EVENT_OBJECT_PARENTCHANGE = 0x800F,

            /** A container object has added, removed, or reordered its children. The system sends this event for the following user
* interface elements: header control, list-view control, toolbar control, and window object. Server applications send this * event as appropriate for their accessible objects. * For example, this event is generated by a list-view object when the number of child elements or the order of the elements changes. * This event is also sent by a parent window when the Z-order for the child windows changes. */
            EVENT_OBJECT_REORDER = 0x8004,

            /** The selection within a container object has changed. The system sends this event for the following user interface elements:
* list-view control, tab control, tree view control, and window object. Server applications send this event for their accessible * objects. * This event signals a single selection: either a child is selected in a container that previously did not contain any selected children, * or the selection has changed from one child to another. * The hwnd and idObject parameters of the WinEventProc callback function describe the container; the idChild parameter identifies the object * that is selected. If the selected child is a window that also contains objects, the idChild parameter is OBJID_WINDOW. */
            EVENT_OBJECT_SELECTION = 0x8006,

            /** A child within a container object has been added to an existing selection. The system sends this event for the following user
* interface elements: list box, list-view control, and tree view control. Server applications send this event for their accessible * objects. * The hwnd and idObject parameters of the WinEventProc callback function describe the container. The idChild parameter is the child that * is added to the selection. */
            EVENT_OBJECT_SELECTIONADD = 0x8007,

            /** An item within a container object has been removed from the selection. The system sends this event for the following user
* interface elements: list box, list-view control, and tree view control. Server applications send this event for their accessible * objects. * This event signals that a child is removed from an existing selection. * The hwnd and idObject parameters of the WinEventProc callback function describe the container; the idChild parameter identifies * the child that has been removed from the selection. */
            EVENT_OBJECT_SELECTIONREMOVE = 0x8008,

            /** Numerous selection changes have occurred within a container object. The system sends this event for list boxes; server
* applications send it for their accessible objects. * This event is sent when the selected items within a control have changed substantially. The event informs the client * that many selection changes have occurred, and it is sent instead of several * EVENT_OBJECT_SELECTIONADD or EVENT_OBJECT_SELECTIONREMOVE events. The client * queries for the selected items by calling the container object's IAccessible::get_accSelection method and * enumerating the selected items. For this event notification, the hwnd and idObject parameters of the WinEventProc callback * function describe the container in which the changes occurred. */
            EVENT_OBJECT_SELECTIONWITHIN = 0x8009,

            /** A hidden object is shown. The system sends this event for the following user interface elements: caret, cursor, and window
* object. Server applications send this event for their accessible objects. * Clients assume that when this event is sent by a parent object, all child objects are already displayed. * Therefore, server applications do not send this event for the child objects. * Hidden objects include the STATE_SYSTEM_INVISIBLE flag; shown objects do not include this flag. * The EVENT_OBJECT_SHOW event also indicates that the STATE_SYSTEM_INVISIBLE flag is cleared. Therefore, servers * do not send the EVENT_STATE_CHANGE event in this case. */
            EVENT_OBJECT_SHOW = 0x8002,

            /** An object's state has changed. The system sends this event for the following user interface elements: check box, combo box,
* header control, push button, radio button, scroll bar, toolbar control, tree view control, up-down control, and window object. * Server applications send this event for their accessible objects. * For example, a state change occurs when a button object is clicked or released, or when an object is enabled or disabled. * For this event notification, the idChild parameter of the WinEventProc callback function identifies the child object whose state has changed. */
            EVENT_OBJECT_STATECHANGE = 0x800A,

            /** The conversion target within an IME composition has changed. The conversion target is the subset of the IME composition
* which is actively selected as the target for user-initiated conversions. */
            EVENT_OBJECT_TEXTEDIT_CONVERSIONTARGETCHANGED = 0x8030,

            /** An object's text selection has changed. This event is supported by common controls and is used by UI Automation.
* The hwnd, ID, and idChild parameters of the WinEventProc callback function describe the item that is contained in the updated text selection. */
            EVENT_OBJECT_TEXTSELECTIONCHANGED = 0x8014,

            /** Sent when a window is uncloaked. A cloaked window still exists, but is invisible to the user.
*/
            EVENT_OBJECT_UNCLOAKED = 0x8018,

            /** An object's Value property has changed. The system sends this event for the user interface elements that include the scroll
* bar and the following controls: edit, header, hot key, progress bar, slider, and up-down. Server applications send this event * for their accessible objects. */
            EVENT_OBJECT_VALUECHANGE = 0x800E,

            /** The range of event constant values reserved for OEMs. For more information, see Allocation of WinEvent IDs.
*/
            EVENT_OEM_DEFINED_START = 0x0101,
            EVENT_OEM_DEFINED_END = 0x01FF,

            /** An alert has been generated. Server applications should not send this event.
*/
            EVENT_SYSTEM_ALERT = 0x0002,

            /** A preview rectangle is being displayed.
*/
            EVENT_SYSTEM_ARRANGMENTPREVIEW = 0x8016,

            /** A window has lost mouse capture. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_CAPTUREEND = 0x0009,

            /** A window has received mouse capture. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_CAPTURESTART = 0x0008,

            /** A window has exited context-sensitive Help mode. This event is not sent consistently by the system.
*/
            EVENT_SYSTEM_CONTEXTHELPEND = 0x000D,

            /** A window has entered context-sensitive Help mode. This event is not sent consistently by the system.
*/
            EVENT_SYSTEM_CONTEXTHELPSTART = 0x000C,

            /** The active desktop has been switched.
*/
            EVENT_SYSTEM_DESKTOPSWITCH = 0x0020,

            /** A dialog box has been closed. The system sends this event for standard dialog boxes; servers send it for custom dialog boxes.
* This event is not sent consistently by the system. */
            EVENT_SYSTEM_DIALOGEND = 0x0011,

            /** A dialog box has been displayed. The system sends this event for standard dialog boxes, which are created using resource
* templates or Win32 dialog box functions. Servers send this event for custom dialog boxes, which are windows that function as * dialog boxes but are not created in the standard way. * This event is not sent consistently by the system. */
            EVENT_SYSTEM_DIALOGSTART = 0x0010,

            /** An application is about to exit drag-and-drop mode. Applications that support drag-and-drop operations must send this event;
* the system does not send this event. */
            EVENT_SYSTEM_DRAGDROPEND = 0x000F,

            /** An application is about to enter drag-and-drop mode. Applications that support drag-and-drop operations must send this
* event because the system does not send it. */
            EVENT_SYSTEM_DRAGDROPSTART = 0x000E,

            /** The highest system event value.
*/
            EVENT_SYSTEM_END = 0x00FF,

            /** The foreground window has changed. The system sends this event even if the foreground window has changed to another window
* in the same thread. Server applications never send this event. * For this event, the WinEventProc callback function's hwnd parameter is the handle to the window that is in the * foreground, the idObject parameter is OBJID_WINDOW, and the idChild parameter is CHILDID_SELF. */
            EVENT_SYSTEM_FOREGROUND = 0x0003,

            /** A pop-up menu has been closed. The system sends this event for standard menus; servers send it for custom menus.
* When a pop-up menu is closed, the client receives this message, and then the EVENT_SYSTEM_MENUEND event. * This event is not sent consistently by the system. */
            EVENT_SYSTEM_MENUPOPUPEND = 0x0007,

            /** A pop-up menu has been displayed. The system sends this event for standard menus, which are identified by HMENU, and are
* created using menu-template resources or Win32 menu functions. Servers send this event for custom menus, which are user * interface elements that function as menus but are not created in the standard way. This event is not sent consistently by the system. */
            EVENT_SYSTEM_MENUPOPUPSTART = 0x0006,

            /** A menu from the menu bar has been closed. The system sends this event for standard menus; servers send it for custom menus.
* For this event, the WinEventProc callback function's hwnd, idObject, and idChild parameters refer to the control * that contains the menu bar or the control that activates the context menu. The hwnd parameter is the handle to the window * that is related to the event. The idObject parameter is OBJID_MENU or OBJID_SYSMENU for a menu, or OBJID_WINDOW for a * pop-up menu. The idChild parameter is CHILDID_SELF. */
            EVENT_SYSTEM_MENUEND = 0x0005,

            /** A menu item on the menu bar has been selected. The system sends this event for standard menus, which are identified
* by HMENU, created using menu-template resources or Win32 menu API elements. Servers send this event for custom menus, * which are user interface elements that function as menus but are not created in the standard way. * For this event, the WinEventProc callback function's hwnd, idObject, and idChild parameters refer to the control * that contains the menu bar or the control that activates the context menu. The hwnd parameter is the handle to the window * related to the event. The idObject parameter is OBJID_MENU or OBJID_SYSMENU for a menu, or OBJID_WINDOW for a pop-up menu. * The idChild parameter is CHILDID_SELF.The system triggers more than one EVENT_SYSTEM_MENUSTART event that does not always * correspond with the EVENT_SYSTEM_MENUEND event. */
            EVENT_SYSTEM_MENUSTART = 0x0004,

            /** A window object is about to be restored. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_MINIMIZEEND = 0x0017,

            /** A window object is about to be minimized. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_MINIMIZESTART = 0x0016,

            /** The movement or resizing of a window has finished. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_MOVESIZEEND = 0x000B,

            /** A window is being moved or resized. This event is sent by the system, never by servers.
*/
            EVENT_SYSTEM_MOVESIZESTART = 0x000A,

            /** Scrolling has ended on a scroll bar. This event is sent by the system for standard scroll bar controls and for
* scroll bars that are attached to a window. Servers send this event for custom scroll bars, which are user interface * elements that function as scroll bars but are not created in the standard way. * The idObject parameter that is sent to the WinEventProc callback function is OBJID_HSCROLL for horizontal scroll bars, and * OBJID_VSCROLL for vertical scroll bars. */
            EVENT_SYSTEM_SCROLLINGEND = 0x0013,

            /** Scrolling has started on a scroll bar. The system sends this event for standard scroll bar controls and for scroll
* bars attached to a window. Servers send this event for custom scroll bars, which are user interface elements that * function as scroll bars but are not created in the standard way. * The idObject parameter that is sent to the WinEventProc callback function is OBJID_HSCROLL for horizontal scrolls bars, * and OBJID_VSCROLL for vertical scroll bars. */
            EVENT_SYSTEM_SCROLLINGSTART = 0x0012,

            /** A sound has been played. The system sends this event when a system sound, such as one for a menu,
* is played even if no sound is audible (for example, due to the lack of a sound file or a sound card). * Servers send this event whenever a custom UI element generates a sound. * For this event, the WinEventProc callback function receives the OBJID_SOUND value as the idObject parameter. */
            EVENT_SYSTEM_SOUND = 0x0001,

            /** The user has released ALT+TAB. This event is sent by the system, never by servers.
* The hwnd parameter of the WinEventProc callback function identifies the window to which the user has switched. * If only one application is running when the user presses ALT+TAB, the system sends this event without a corresponding * EVENT_SYSTEM_SWITCHSTART event. */
            EVENT_SYSTEM_SWITCHEND = 0x0015,

            /** The user has pressed ALT+TAB, which activates the switch window. This event is sent by the system, never by servers.
* The hwnd parameter of the WinEventProc callback function identifies the window to which the user is switching. * If only one application is running when the user presses ALT+TAB, the system sends an EVENT_SYSTEM_SWITCHEND event without a * corresponding EVENT_SYSTEM_SWITCHSTART event. */
            EVENT_SYSTEM_SWITCHSTART = 0x0014,

            /** The range of event constant values reserved for UI Automation event identifiers. For more information,
* see Allocation of WinEvent IDs. */
            EVENT_UIA_EVENTID_START = 0x4E00,
            EVENT_UIA_EVENTID_END = 0x4EFF,

            /**
* The range of event constant values reserved for UI Automation property-changed event identifiers. * For more information, see Allocation of WinEvent IDs. */
            EVENT_UIA_PROPID_START = 0x7500,
            EVENT_UIA_PROPID_END = 0x75FF
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(WinEvents eventMin, WinEvents eventMax, IntPtr
                hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
            uint idThread, WinEventFlags dwFlags);

        // [DllImport("user32.dll")]
        // [return: MarshalAs(UnmanagedType.Bool)]
        // public static extern bool GetClientRect(IntPtr hWnd, ref Rect rect);
    }


    public class GameOverlayManager
    {
        private NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
#if DEV
        public static string GAME_PROCESS = "notepad";
#else
        public static string GAME_PROCESS = "dirtrally2";
        public static string GAME_WIN_TITLE = "Dirt Rally 2.0";
#endif
        private StickyWindow _window;

        private readonly Dictionary<string, SolidBrush> _brushes = new();
        private readonly Dictionary<string, Font> _fonts = new();
        private readonly Dictionary<string, Image> _images = new();
        private BackgroundWorker _bgw;
        private bool _isRunning;
        

        private Random _random;

        public string TrackName { set; get; } = "";
        public string AudioPackage { set; get; } = "";
        public string ScriptAuthor { set; get; } = "";
        public string PacenoteType { set; get; } = "";

        private bool _TimeToShowTelemetry = false;

        public bool TimeToShowTelemetry
        {
            set
            {
                _TimeToShowTelemetry = value;
                maxSpeed = MAX_SPEED;
                maxWheelSpeed = MAX_WHEEL_SPEED;
                maxWheelTemp = MAX_WHEEL_TEMP;
                maxGForce = MAX_G_FORCE;
                maxSuspension = MAX_SUSPENSION;
                minSuspension = MIN_SUSPENSION;
                maxSuspensionSpd = MAX_SUSPENSION_SPD;
                minSuspensionSpd = MIN_SUSPENSION_SPD;
            }
            get
            {
                return _TimeToShowTelemetry;
            }
        }

        public bool TimeToShowStatistics { set; get; }

        public GameData GameData { set; get; }

        public static float MAX_SPEED = 200f;
        public static float MAX_WHEEL_SPEED = 220f;
        public static float MAX_WHEEL_TEMP = 800f;
        public static float MAX_G_FORCE = 2.2f;
        public static float MAX_SUSPENSION_SPD = 1000f; // m/s
        public static float MIN_SUSPENSION_SPD = -1000f;
        public static float MAX_SUSPENSION = 200;
        public static float MIN_SUSPENSION = -200;
        
        private float maxSpeed { set; get; }
        private float maxWheelSpeed { set; get; }
        private float maxWheelTemp { set; get; }
        private float maxGForce { set; get; }
        private float maxSuspensionSpd { set; get; }
        private float minSuspensionSpd { set; get; }
        private float maxSuspension { set; get; }
        private float minSuspension { set; get; }

        public void InitializeOverlay(System.Diagnostics.Process process)
        {
#if DEV
            UdpMessage = new UDPMessage()
            {
                Brake = 0.5f,
                Throttle = 0.3f,
                Clutch = 0.9f,
                RPM = 6300f,
                MaxRPM = 9000f,
                IdleRPM = 1000f,
                Speed = 130f,
                Gear = 3f,
                MaxGears = 4,
                G_lat = 0.5f,
                G_long = 0.2f,
                SpeedFrontLeft = 190f,
                SpeedRearLeft = 28f,
                SpeedFrontRight = 181f,
                SpeedRearRight = 59f,
                BrakeTempFrontLeft = 99,
                BrakeTempFrontRight = 98,
                BrakeTempRearLeft = 90,
                BrakeTempRearRight = 91,
                SuspensionFrontLeft = 0.8f,
                SuspensionFrontRight = 0.7f,
                SuspensionRearLeft = 0.75f,
                SuspensionRearRight = 0.71f,
                CompletionRate = 0.35f,
                Steering = 0.9f,
                SuspensionSpeedFrontLeft = 20,
                SuspensionSpeedFrontRight = 23,
                SuspensionSpeedRearLeft = 35,
                SuspensionSpeedRearRight = 46,
                Sector = 1,
                Time = 20,
                CarPos = 30,
                CurrentLap = 1,
                LapDistance = 200,
                LapsComplete = 1,
                LapTime = 20,
                PitchX = 234,
                PitchY = 324,
                PitchZ = 232,
                PosX = 245,
                PosY = 425,
                PosZ = 5356,
                RollX = 24,
                RollY = 90,
                RollZ = 425,
                Sector1Time = 835,
                Sector2Time = 835,
                SpeedX = 5753,
                SpeedY = 53,
                SpeedZ = 3536,
                TimeStamp = DateTime.Now,
                TotalLaps = 4,
                TrackLength = 35667,
                LastLapTime = 36,
            };
            TimeToShowTelemetry = true;
#endif
            var gfx = new Graphics()
            {
                MeasureFPS = false,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true
            };


            _window = new StickyWindow(process.MainWindowHandle, gfx);
            _window.Title = "ZTMZ Club Hud";
            _window.FPS = Config.Instance.HudFPS;   // 50 fps by default
            _window.AttachToClientArea = true;
            if (Config.Instance.HudTopMost) 
            {
                _window.IsTopmost = Config.Instance.HudTopMost;
            } else 
            {
                _window.BypassTopmost = true;
            }
            _window.IsVisible = true;


            _window.DestroyGraphics += _window_DestroyGraphics;
            _window.DrawGraphics += _window_DrawGraphics;
            _window.SetupGraphics += _window_SetupGraphics;

            this.Run();
        }

        private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
        {
            var gfx = e.Graphics;

            if (e.RecreateResources)
            {
                foreach (var pair in _brushes) pair.Value.Dispose();
                foreach (var pair in _images) pair.Value.Dispose();
            }

            _brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
            _brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
            _brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
            _brushes["grey"] = gfx.CreateSolidBrush(64, 64, 64);
        if (Config.Instance.HudChromaKeyMode)
            {
                // change green to blue, blue to purple, clear to green
                _brushes["green"] = gfx.CreateSolidBrush(0, 0, 255);
                _brushes["blue"] = gfx.CreateSolidBrush(255, 0, 255);
                _brushes["clear"] = gfx.CreateSolidBrush(0, 255, 0);
            }
            else
            {
                _brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
                _brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
                _brushes["clear"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 0);
            }
            _brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F, 100);
            

            _brushes["grid"] = gfx.CreateSolidBrush(255, 255, 255, 0.2f);
            _brushes["random"] = gfx.CreateSolidBrush(0, 0, 0);
            
            _brushes["telemetryBackground"] = gfx.CreateSolidBrush(0x3, 0x6, 0xF, 255);

            if (e.RecreateResources) return;

            _fonts["arial"] = gfx.CreateFont("Arial", 12);
            _fonts["consolas"] = gfx.CreateFont("Consolas", 14);

            _fonts["telemetryGear"] = gfx.CreateFont("Consolas", 32);
        }

        private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
        {
            foreach (var pair in _brushes) pair.Value.Dispose();
            foreach (var pair in _fonts) pair.Value.Dispose();
            foreach (var pair in _images) pair.Value.Dispose();
        }

        private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
        {
            var gfx = e.Graphics;
            gfx.ClearScene(_brushes["clear"]);
            //_gridBounds = new Rectangle(20, 60, gfx.Width - 600, gfx.Height - 20);

            try
            {
                if (gfx.Height == 0 || gfx.Width == 0) 
                {
                    // the window is not yet visible
                    return;
                }

                drawBasicInfo(gfx);
                if (Config.Instance.HudShowTelemetry && TimeToShowTelemetry)
                {
                    drawTelemetry(gfx);
                }

                if (Config.Instance.HudShowDebugTelemetry && TimeToShowTelemetry)
                {
                    drawDebugTelemetry(gfx);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("We got exception when drawing hud: {0}", ex.ToString());
                GoogleAnalyticsHelper.Instance.TrackExceptionEvent("We got exception when drawing hud", ex.ToString());
            }
        }

        private void drawBasicInfo(Graphics gfx)
        {
            var padding = 200;
            var infoText = new StringBuilder()
                .Append(I18NLoader.Instance["overlay.track"].PadRight(16)).Append(this.TrackName.PadRight(padding)).AppendLine()
                .Append(I18NLoader.Instance["overlay.audioPackage"].PadRight(16)).Append(this.AudioPackage.PadRight(padding)).AppendLine()
                .Append(I18NLoader.Instance["overlay.scriptAuthor"].PadRight(16)).Append(this.ScriptAuthor.PadRight(padding)).AppendLine()
                .Append(I18NLoader.Instance["overlay.dyanmic"].PadRight(16)).Append(this.PacenoteType.PadRight(padding))
                .ToString();
            var size = gfx.MeasureString(_fonts["consolas"], _fonts["consolas"].FontSize, infoText);
            gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], gfx.Width - size.X, 0, infoText);
        }

        private void drawDebugTelemetry(Graphics gfx)
        {
            gfx.DrawTextWithBackground(_fonts["consolas"], _brushes["green"], _brushes["background"], 0, 0, GameData.ToString());
        }

        private void drawTelemetry(Graphics gfx)
        {
            List<Action<Graphics, float, float, float, float>> drawFuncs = new();
            if (Config.Instance.HudTelemetryShowGBall) drawFuncs.Add(drawGBall);
            if (Config.Instance.HudTelemetryShowSpdSector) drawFuncs.Add(drawSpdSector);
            if (Config.Instance.HudTelemetryShowRPMSector) drawFuncs.Add(drawRPMSector);
            if (Config.Instance.HudTelemetryShowPedals) drawFuncs.Add(drawPedals);
            if (Config.Instance.HudTelemetryShowGear) drawFuncs.Add(drawGear);
            if (Config.Instance.HudTelemetryShowSteering) drawFuncs.Add(drawSteering);
            if (Config.Instance.HudTelemetryShowSuspensionBars) drawFuncs.Add(drawSuspensionBars);
            
            // calculate the margin, padding, pos of each element
            var telemetryHeight = gfx.Height * Config.Instance.HudSizePercentage;
            var telemetryWidth = telemetryHeight * drawFuncs.Count; // elements are squre?
            var telemetryStartPosX = 0.5f * (gfx.Width - telemetryWidth);
            var telemetryStartPosY = gfx.Height - telemetryHeight;

            var telemetryPaddingH = telemetryHeight * Config.Instance.HudPaddingH;
            var telemetryPaddingV = telemetryHeight * Config.Instance.HudPaddingV;

            var telemetrySpacing = telemetryHeight * Config.Instance.HudElementSpacing;
            
            // drawBackground
            _brushes["telemetryBackground"].Color = new Color(
                _brushes["telemetryBackground"].Color.R,
                _brushes["telemetryBackground"].Color.G,
                _brushes["telemetryBackground"].Color.B,
                255 * Config.Instance.HudBackgroundOpacity);
            gfx.FillRectangle(_brushes["telemetryBackground"], 
                telemetryStartPosX,
                telemetryStartPosY,
                telemetryStartPosX + telemetryWidth,
                telemetryStartPosY + telemetryHeight);

            var elementStartX = telemetryStartPosX + telemetryPaddingH;
            var elementStartY = telemetryStartPosY + telemetryPaddingV;
            var elementHeight = telemetryHeight - telemetryPaddingV * 2f;
            var elementWidth = ((telemetryWidth - telemetryPaddingH * 2f) - (drawFuncs.Count-1) * telemetrySpacing) /
                               drawFuncs.Count;

            foreach (var t in drawFuncs)
            {

                try
                {
                    //TODO: suppress unknown ex for now, I have no env for testing...
                    t(gfx, elementStartX, elementStartY, elementWidth, elementHeight);
                }
                catch (Exception ex)
                {
                    _logger.Error("We got exception when drawing elements: {0}, {1}", ex.ToString(), GameData.ToString());
                    // GoogleAnalyticsHelper.Instance.TrackExceptionEvent($"We got exception when drawing elements with func: {t.ToString()}", ex.Message + UdpMessage.ToString());
                }

                elementStartX += elementWidth + telemetrySpacing;
            }
            
            // draw the finish rate
            gfx.FillRectangle(_brushes["green"], 
                0, 
                telemetryStartPosY + telemetryHeight - 5, 
                GameData.CompletionRate * gfx.Width,
                telemetryStartPosY + telemetryHeight);
        }

        private void drawGBall(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radius = MathF.Min(width, height) * 0.5f;
            gfx.FillCircle(_brushes["grey"], centerX, centerY, radius);
            gfx.DrawLine(_brushes["grid"], centerX - radius, centerY, centerX + radius, centerY, 1);
            gfx.DrawLine(_brushes["grid"], centerX, centerY - radius, centerX, centerY + radius, 1);
            gfx.DrawCircle(_brushes["white"], centerX, centerY, radius -1 , 1);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radius , 1);
            // the ball
            var ballX = centerX + GameData.G_lat * radius / MAX_G_FORCE;
            var ballY = centerY + GameData.G_long * radius / MAX_G_FORCE;
            gfx.FillCircle(_brushes["red"], ballX, ballY, radius * 0.1f);
        }
        private void drawSpdSector(Graphics gfx, float x, float y, float width, float height)
        {
            maxSpeed = MathF.Max(maxSpeed, GameData.Speed); 
            drawSector(gfx, "SPD (KM/h)", x, y, width, height, GameData.Speed, maxSpeed, Config.Instance.HudSectorThicknessRatio);
        }
        private void drawPedals(Graphics gfx, float x, float y, float width, float height)
        {
            // 3 pedals
            var pedalWidth = 1f / 3.6f * width;
            var spacing = 0.3f / 3.6f * width;
            gfx.FillRectangle(_brushes["grey"], x, y, x + pedalWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height);
            gfx.FillRectangle(_brushes["grey"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height);
            gfx.DrawRectangle(_brushes["white"], x, y, x + pedalWidth, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + pedalWidth + spacing, y, x + 2 * pedalWidth + spacing, y + height, 1);
            gfx.DrawRectangle(_brushes["white"], x + 2 * pedalWidth + 2 * spacing, y, x + width, y + height, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + pedalWidth+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + pedalWidth + spacing-1, y-1, x + 2 * pedalWidth + spacing+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x + 2 * pedalWidth + 2 * spacing-1, y-1, x + width+1, y + height+1, 1);

            gfx.FillRectangle(_brushes["blue"], 1 + x, 1 + y + height * (1-GameData.Clutch), x + pedalWidth - 1, y + height - 1);
            gfx.FillRectangle(_brushes["red"], 1 + x + pedalWidth + spacing, 1 + y + height * (1-GameData.Brake), x + 2 * pedalWidth + spacing - 1, y + height - 1);
            gfx.FillRectangle(_brushes["green"], 1 + x + 2 * pedalWidth + 2 * spacing, 1 + y + height * (1-GameData.Throttle), x + width - 1, y + height - 1);

        }
        private void drawGear(Graphics gfx, float x, float y, float width, float height)
        {
            // var font = gfx.CreateFont("consolas", width);
            // var actualSize = MathF.Min(width, height);
            // gfx.DrawText(_fonts["consolas"], actualSize, _brushes["white"], x, y, getGearText(Convert.ToInt32(UdpMessage.Gear)));
            var columns = Convert.ToInt32(MathF.Ceiling(GameData.MaxGears * 0.5f)) + 1;

            var barWidth = width / (columns + (columns-1) * Config.Instance.HudSectorThicknessRatio);
            var spacingH = Config.Instance.HudSectorThicknessRatio * barWidth;
            var barHeight = height / (2 + Config.Instance.HudSectorThicknessRatio);
            var spacingV = barHeight * Config.Instance.HudSectorThicknessRatio;

            List<Rectangle> rectangles = new()
            {
                // R
                new Rectangle(x, y, x + barWidth, y + barHeight),
            };

            for (var i = 1; i <= GameData.MaxGears; i++)
            {
                var row = (i + 1) % 2;
                var column = (i + 1) / 2;
                rectangles.Add(new Rectangle(
                    x + column * (spacingH + barWidth),
                    y + row * (spacingV + barHeight),
                    x + barWidth + column * (spacingH + barWidth),
                    y + barHeight + row * (spacingV + barHeight)
                    ));
            }

            foreach (var r in rectangles)
            {
                gfx.FillRectangle(_brushes["white"], r);
                gfx.DrawRectangle(_brushes["black"], r, 1);
            }

            int gear = Convert.ToInt32(GameData.Gear);
            string gearText = "";
            bool isNGear = false;
            Rectangle rect;
            switch (gear) 
            {
                case -1:
                case 10:
                    rect = rectangles[0];
                    gearText = "R";
                    break;
                case 0:
                    rect = rectangles[0];
                    isNGear = true;
                    gearText = "N";
                    break;
                default:
                    rect = rectangles[gear];
                    gearText = gear.ToString();
                    break;
            }
            if (!isNGear)
            {
                gfx.FillRectangle(_brushes["red"], rect.Left + 1, rect.Top + 1, rect.Right - 1, rect.Bottom - 1);
            }
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                barWidth * 1.5f,
                _brushes["white"],
                _brushes["black"],
                x + 0.5f * barWidth,
                y + spacingV + 1.5f * barHeight,
                gearText);
        }

        private void drawSteering(Graphics gfx, float x, float y, float width, float height)
        {
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - Config.Instance.HudSectorThicknessRatio);
            var radiusWidth = radiusOuter - radiusInner;

            var rawSteeringAngle = GameData.Steering * Config.Instance.HudTelemetrySteeringDegree * 0.5f;
            // bg
            IBrush pathBrush;
            IBrush bgBrush;
            if (MathF.Abs(rawSteeringAngle) >= 360)
            {
                pathBrush = _brushes["blue"];
                bgBrush = _brushes["white"];
            }
            else
            {
                pathBrush = _brushes["white"];
                bgBrush = _brushes["grey"];
            }
            gfx.FillCircle(bgBrush, centerX, centerY, radiusOuter);
            
            
            var steeringAngle = 90 - rawSteeringAngle;
            steeringAngle = steeringAngle / 180 * MathF.PI; // to radian
            var middle = 0.5f * (radiusInner + radiusOuter);

            
            
            gfx.FillCircle(_brushes["black"], centerX, centerY, radiusInner);
            gfx.DrawCircle(_brushes["black"], centerX, centerY, radiusOuter, 1);

            var anchorLeft = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI * 0.5f));
            var anchorRight = new Point(centerX + middle * MathF.Cos(steeringAngle - MathF.PI * 0.5f),
                centerY - middle * MathF.Sin(steeringAngle - MathF.PI * 0.5f));
            var anchorBottom = new Point(centerX + middle * MathF.Cos(steeringAngle + MathF.PI),
                centerY - middle * MathF.Sin(steeringAngle + MathF.PI));
            
            // cross
            gfx.DrawLine(bgBrush, anchorLeft, anchorRight, radiusWidth);
            gfx.DrawLine(bgBrush, centerX, centerY, anchorBottom.X, anchorBottom.Y, radiusWidth);

            // cursor
            var alpha = MathF.PI / 30f;
            radiusOuter -= 1;
            
            // path
            var arcSize = MathF.Abs(rawSteeringAngle) % 360 >= 180 ? ArcSize.Large : ArcSize.Small;
            var sweepDirection = rawSteeringAngle > 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var backsDirection = rawSteeringAngle < 0 ? SweepDirection.Clockwise : SweepDirection.CounterClockwise;
            var geo_path = gfx.CreateGeometry();
            geo_path.BeginFigure(new Point(centerX,
                centerY - radiusOuter), true);
            geo_path.addCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle),
                centerY - radiusOuter * MathF.Sin(steeringAngle)), radiusOuter, arcSize, sweepDirection);
            geo_path.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle),
                centerY - radiusInner * MathF.Sin(steeringAngle)));
            geo_path.addCurve(new Point(centerX,
                centerY - radiusInner), radiusInner, arcSize, backsDirection);
            geo_path.EndFigure();
            geo_path.Close();

            gfx.FillGeometry(geo_path, pathBrush);
            
            var geo_cur = gfx.CreateGeometry();
            geo_cur.BeginFigure(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle + alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle + alpha)), true);
            geo_cur.addCurve(new Point(centerX + radiusOuter * MathF.Cos(steeringAngle - alpha),
                centerY - radiusOuter * MathF.Sin(steeringAngle - alpha)), radiusOuter, ArcSize.Small, SweepDirection.Clockwise);
            geo_cur.AddPoint(new Point(centerX + radiusInner * MathF.Cos(steeringAngle - alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle - alpha)));
            geo_cur.addCurve(new Point(centerX + radiusInner * MathF.Cos(steeringAngle + alpha),
                centerY - radiusInner * MathF.Sin(steeringAngle + alpha)), radiusInner, ArcSize.Small, SweepDirection.CounterClockwise);
            geo_cur.EndFigure();
            geo_cur.Close();
            
            gfx.FillGeometry(geo_cur, _brushes["red"]);
        }
        
        private string getGearText(int g)
        {
            return g switch
            {
                -1 => "R",
                10 => "R",
                0 => "N",
                _ => g.ToString()
            };
        }
        private void drawRPMSector(Graphics gfx, float x, float y, float width, float height)
        {
            var t = drawSector(gfx, "RPM", x, y, width, height, GameData.RPM, GameData.MaxRPM, Config.Instance.HudSectorThicknessRatio);

        }
        private void drawSuspensionBars(Graphics gfx, float x, float y, float width, float height)
        {
            // wheel spd, suspension, wheel temp
            var bgWidth = 0.3f * width;
            var bgHeight = 0.45f * height;
            var spacingH = 0.4f * width;
            var spacingV = 0.1f * height;
            var barWidth = bgWidth / 4f;

            // background
            gfx.FillRectangle(_brushes["grey"], x, y, x + bgWidth, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y, x + width, y + bgHeight);
            gfx.FillRectangle(_brushes["grey"], x, y + bgHeight + spacingV, x + bgWidth, y + height);
            gfx.FillRectangle(_brushes["grey"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height);
            // gfx.DrawRectangle(_brushes["white"], x-1, y-1, x + width+1, y + height+1, 1);
            gfx.DrawRectangle(_brushes["black"], x-1, y-1, x + width+1, y + height+1, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["black"], x, y + bgHeight + spacingV, x + bgWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["black"], x + bgWidth + spacingH, y + bgHeight + spacingV, x + width, y + height, 1);
            
            
            // wheel spd
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight, x + barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight, x + width, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["green"], x, y + bgHeight + spacingV + bgHeight, x + barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + bgHeight, x + width, y + height, 1);
            // x += 1;
            // y += 1;
            // width -= 2;
            // height -= 2;
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontLeft);
            gfx.FillRectangle(_brushes["green"], x, y + (1-GameData.SpeedFrontLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedFrontRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + (1-GameData.SpeedFrontRight / maxWheelSpeed) * bgHeight, x + width, y + bgHeight);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearLeft);
            gfx.FillRectangle(_brushes["green"], x, y + bgHeight + spacingV + (1-GameData.SpeedRearLeft / maxWheelSpeed) * bgHeight, x + barWidth, y + height);
            maxWheelSpeed = MathF.Max(maxWheelSpeed, GameData.SpeedRearRight);
            gfx.FillRectangle(_brushes["green"], x + width - barWidth, y + bgHeight + spacingV + (1-GameData.SpeedRearRight / maxWheelSpeed) * bgHeight, x + width, y + height);
            
            // brake temp
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight, x + 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight, x + width - barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + bgHeight, x + 2 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - barWidth, y + height, 1);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontLeft)) * bgHeight, x + 2 * barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempFrontRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempFrontRight)) * bgHeight, x + width - barWidth, y + bgHeight);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearLeft);
            gfx.FillRectangle(_brushes["red"], x + barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearLeft)) * bgHeight, x + 2 * barWidth, y + height);
            maxWheelTemp = MathF.Max(maxWheelTemp, GameData.BrakeTempRearRight);
            gfx.FillRectangle(_brushes["red"], x + width - 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxWheelTemp, 0, GameData.BrakeTempRearRight)) * bgHeight, x + width - barWidth, y + height);

            // suspension
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight, x + 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight, x + width - 2 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + bgHeight, x + 3 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 2 * barWidth, y + height, 1);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontLeft)) * bgHeight, x + 3 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionFrontRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionFrontRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionFrontRight)) * bgHeight, x + width - 2 * barWidth, y + bgHeight);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearLeft);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearLeft);
            gfx.FillRectangle(_brushes["white"], x + 2 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearLeft)) * bgHeight, x + 3 * barWidth, y + height);
            minSuspension = MathF.Min(minSuspension, GameData.SuspensionRearRight);
            maxSuspension = MathF.Max(maxSuspension, GameData.SuspensionRearRight);
            gfx.FillRectangle(_brushes["white"], x + width - 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspension, minSuspension, GameData.SuspensionRearRight)) * bgHeight, x + width - 2 * barWidth, y + height);
            
            // suspension_speed
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight, x + 4 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight, x + width - 3 * barWidth, y + bgHeight, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + bgHeight, x + 4 * barWidth, y + height, 1);
            // gfx.DrawRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + bgHeight, x + width - 3 * barWidth, y + height, 1);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontLeft)) * bgHeight, x + 4 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedFrontRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedFrontRight)) * bgHeight, x + width - 3 * barWidth, y + bgHeight);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearLeft);
            gfx.FillRectangle(_brushes["blue"], x + 3 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearLeft)) * bgHeight, x + 4 * barWidth, y + height);
            maxSuspensionSpd = MathF.Max(maxSuspensionSpd, GameData.SuspensionSpeedRearRight);
            minSuspensionSpd = MathF.Min(minSuspensionSpd, GameData.SuspensionSpeedRearRight);
            gfx.FillRectangle(_brushes["blue"], x + width - 4 * barWidth, y + bgHeight + spacingV + (1-getMaxMinBarPartition(maxSuspensionSpd, minSuspensionSpd, GameData.SuspensionSpeedRearRight)) * bgHeight, x + width - 3 * barWidth, y + height);

        }

        private float getMaxMinBarPartition(float max, float min, float value)
        {
            value = value < min ? min : value;
            value = value > max ? max : value;
            return (value - min) / (max - min);
        }

        private Point drawSector(Graphics gfx, 
            string unit,
            float x, 
            float y, 
            float width, 
            float height, 
            float value, 
            float maxValue, 
            float thicknessRatio=0.2f)
        {
            if (value > maxValue)
            {
                value = maxValue;
            }
            if (value < 0)
            {
                value = 0;
            }
            var centerX = x + 0.5f * width;
            var centerY = y + 0.5f * height;
            var radiusOuter = MathF.Min(width, height) * 0.5f;
            var radiusInner = radiusOuter * (1 - thicknessRatio);
            // draw backgroud geometry
            var geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                    ),
                true
                );
            geo_bg.addCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.75f)
                ),
                radiusOuter, ArcSize.Large
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.75f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.75f)
                )
            );
            geo_bg.addCurve(
                new Point(
                    centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                    centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
                ),
                radiusInner, ArcSize.Large, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["grey"]);
            gfx.DrawGeometry(geo_bg, _brushes["black"], 1);
            
            // draw value
            var angle = MathF.PI * 1.25f - value / maxValue * MathF.PI * 1.5f;
            var arcSize = value / maxValue * 270f >= 180f ? ArcSize.Large : ArcSize.Small;
            radiusOuter -= 1;
            radiusInner += 1;
            geo_bg = gfx.CreateGeometry();
            geo_bg.BeginFigure(
                new Point(
                    centerX + radiusOuter * MathF.Cos(MathF.PI * 1.25f), 
                    centerY - radiusOuter * MathF.Sin(MathF.PI * 1.25f)
                ),
                true
            );
            geo_bg.addCurve(
                new Point(
                    centerX + radiusOuter * MathF.Cos(angle),
                    centerY - radiusOuter * MathF.Sin(angle)
                ),
                radiusOuter, arcSize
            );
            geo_bg.AddPoint(
                new Point(
                    centerX + radiusInner * MathF.Cos(angle),
                    centerY - radiusInner * MathF.Sin(angle)
                )
            );
            var endPoint = new Point(
                centerX + radiusInner * MathF.Cos(MathF.PI * 1.25f),
                centerY - radiusInner * MathF.Sin(MathF.PI * 1.25f)
            );
            geo_bg.addCurve(
                endPoint,
                radiusInner, arcSize, SweepDirection.CounterClockwise
            );
            geo_bg.EndFigure();
            geo_bg.Close();
            gfx.FillGeometry(geo_bg, _brushes["white"]);

            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.2f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY + radiusInner,
                unit);
            gfx.drawTextWithBackgroundCentered(_fonts["consolas"],
                0.5f * radiusOuter,
                _brushes["white"],
                _brushes["black"],
                centerX,
                centerY,
                Convert.ToInt32(value).ToString());

            return endPoint;
        }

        private SolidBrush GetRandomColor()
        {
            var brush = _brushes["random"];

            brush.Color = new Color(_random.Next(0, 256), _random.Next(0, 256), _random.Next(0, 256));

            return brush;
        }

        public void Run()
        {
            _window.Create();
            _window.Show();
            // _window.Join();
        }

        ~GameOverlayManager()
        {
            Dispose(false);
        }

        #region IDisposable Support

        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                _window?.Dispose();

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        public void StartLoop()
        {
            _isRunning = true;
            _bgw = new BackgroundWorker();
            _bgw.DoWork += (sender, args) =>
            {
                while (_isRunning)
                {
                    // 1. find process   
                    Thread.Sleep(5000);
                    var processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);

                    if (processes.Length > 0 && _window == null)
                    {
                        // dr2 has 2 windows during launching...shit
                        processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                        var process = processes.First();
                        try {
                            if (process.MainWindowTitle.StartsWith(GAME_WIN_TITLE, StringComparison.OrdinalIgnoreCase))
                            // only when the game window available.
                                this.InitializeOverlay(process);
                        } catch (Exception ex) {
                            _logger.Trace("Waiting for game window: {0}", GAME_WIN_TITLE);
                        }
                    }

                    if (processes.Length == 0 && _window != null)
                    {
                        // destroy the window
                        _window.Dispose();
                        _window = null;
                    }

                    //if (processes.Length > 0 && _window != null)
                    //{
                    //    // running
                    //    // check full screen ?
                    //    if (WindowHelper.GetForegroundWindow() != _window.Handle)
                    //    {
                    //        try
                    //        {
                    //            Thread.Sleep(3000);
                    //            processes = System.Diagnostics.Process.GetProcessesByName(GAME_PROCESS);
                    //            var process = processes.First();
                    //            WindowHelper.EnableBlurBehind(process.MainWindowHandle);
                    //            _window.IsTopmost = true;
                    //            _window.Recreate();
                    //        }
                    //        catch (Exception e)
                    //        {
                    //            MessageBox.Show("BOOOOOOM");
                    //        }
                    //    }
                    //}
                }
            };
            _bgw.RunWorkerAsync();
        }

        public void StopLoop()
        {
            _bgw?.Dispose();
            _bgw = null;
            _window?.Dispose();
            _window = null;
            _isRunning = false;
        }

    }
}
