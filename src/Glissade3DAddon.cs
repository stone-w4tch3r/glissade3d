using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KSP.UI.Screens;
using UnityEngine;
using UnityEngine.Networking;

namespace Glissade3D;

[KSPAddon(KSPAddon.Startup.Flight, false)]
public sealed class Glissade3DAddon : MonoBehaviour
{
    private const int WindowId = 0x4747;
    private const int EditorId = 0x4748;
    private const float GrabPixels = 16f;
    private const float HoldDelay = 0.45f;
    private const float HoldRepeat = 0.1f;

    private static readonly Color ActiveColor = new Color(0.15f, 0.95f, 0.35f, 1f);
    private static readonly Color EditingColor = new Color(1f, 0.85f, 0.15f, 1f);
    private static readonly Color PointAColor = new Color(0.35f, 0.85f, 1f, 1f);

    private readonly List<GateVisual> _gates = new List<GateVisual>();
    private readonly List<Route> _bodyRoutes = new List<Route>();
    private readonly Dictionary<string, string> _buffers = new Dictionary<string, string>();
    private readonly List<RouteProblem> _problems = new List<RouteProblem>();
    private readonly Dictionary<string, double> _routeDistance = new Dictionary<string, double>();

    private ApproachPath _path;
    private int _pathFrame = -1;
    private string _pathId = "";
    private CelestialBody _pathBody;
    private double _pathLat;
    private double _pathLon;
    private double _pathAlt;
    private double _pathHeading;
    private double _pathAngle;

    private GatesSettings _settings = new GatesSettings();
    private Transform _root;
    private ApplicationLauncherButton _launcher;
    private Texture2D _icon;

    private string _builtKey = "";
    private string _bodyName = "";
    private string _readout = "";
    private float _nextReadout;
    private float _nextAutoSelect;
    private bool _loggedGeometry;
    private bool _rebinding;

    private Vector2 _scroll;
    private Vector2 _contentScroll;
    private Vector2 _editorScroll;
    private string _search = "";
    private string _toast = "";
    private float _toastUntil;
    private string _errorText = "";

    private SharedInstallState _sharedState = new SharedInstallState();
    private bool _sharedBusy;
    private string _sharedStatus = "";
    private bool _confirmUninstall;

    private MarkerVisual _marker;
    private MarkerVisual _markerA;

    private bool _editorOpen;
    private RouteDraft _draft;
    private bool _advancedProfile;
    private int _pickTarget;
    private int _dragMode;
    private int _dragTarget;
    private Vector3d _dragPlanePoint;
    private Vector3d _dragPlaneNormal;
    private Rect _mainRect;
    private Rect _editorRect;
    private bool _confirmDiscard;
    private bool _confirmDelete;
    private float _nudgeMetres = 1f;
    private string _pressId = "";
    private float _holdAt;

    private bool _settingsDirty;
    private float _settingsSaveAt;
    private string _pendingDeleteId = "";
    private bool _launcherFailed;
    private float _nextDistanceRefresh;
    private int _problemsRevision = -1;
    private string _problemsBody = "";
    private double _problemsAngle;
    private float _problemsLogAt;

    private bool _pointMode;
    private bool _markerUnderground;
    private float _nextMarkerLog;
    private float _nextAnchorLog;
    private double _aLat;
    private double _aLon;
    private double _aAlt;
    private bool _hasA;

    private bool _pausedByUs;
    private float _savedTimeScale = 1f;

    private int _smokeStep = -1;
    private float _smokeNext;
    private int _smokeFailures;

    private static Glissade3DAddon _instance;
    private bool _duplicate;
    private bool _started;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            _duplicate = true;
            Debug.Log("[Glissade3D] duplicate addon instance ignored");
            return;
        }
        _instance = this;
    }

    private void Start()
    {
        if (_duplicate) return;
        _settings = GatesSettings.Load();
        Loc.Initialize();
        Dbg.Enabled = _settings.DebugLog || Environment.GetEnvironmentVariable("GLISSADE_DEBUG") == "1";
        _sharedState = SharedInstallState.Load(SharedStatePath);
        if (string.IsNullOrEmpty(SharedBaseUrl())) _sharedStatus = Loc.Get("shared_unconfigured");
        Dbg.Line($"shared base='{SharedBaseUrl()}' managed={_sharedState.Count}");
        Dbg.Line($"start: angle={_settings.DefaultAngle:0.###} auto={_settings.AutoRoute} active={_settings.ActiveRouteId}");
        RouteLibrary.EnsureLoaded(_settings.DefaultAngle);

        var rootObject = new GameObject("Glissade3DRoot");
        DontDestroyOnLoad(rootObject);
        _root = rootObject.transform;
        _marker = new MarkerVisual(_root, "B");
        _markerA = new MarkerVisual(_root, "A");

        RefreshBodyRoutes();
        Debug.Log($"[Glissade3D] started routes={RouteLibrary.Routes.Count} angle={_settings.DefaultAngle}");

        if (Environment.GetEnvironmentVariable("GLISSADE_SMOKE") == "1")
        {
            _smokeStep = 0;
            _smokeNext = Time.unscaledTime + 2f;
            Debug.Log("[Glissade3D][SMOKE] armed");
        }

        if (Environment.GetEnvironmentVariable("GLISSADE_SHARED_AUTOINSTALL") == "1")
            StartCoroutine(SharedAutoInstallSmoke());

        _started = true;
    }

    private IEnumerator SharedAutoInstallSmoke()
    {
        yield return new WaitForSeconds(3f);
        Debug.Log($"[Glissade3D][SMOKE] shared autoinstall base='{SharedBaseUrl()}'");
        if (string.IsNullOrEmpty(SharedBaseUrl()))
        {
            Debug.Log("[Glissade3D][SMOKE] shared autoinstall skipped: no base url");
            yield break;
        }
        yield return StartCoroutine(InstallSharedRoutine());
        Debug.Log($"[Glissade3D][SMOKE] shared autoinstall done status='{_sharedStatus}' managed={_sharedState.Count} routes={RouteLibrary.Routes.Count}");
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        if (_duplicate) return;

        var started = _started;
        if (started && _launcher != null && ApplicationLauncher.Instance != null)
        {
            ApplicationLauncher.Instance.RemoveModApplication(_launcher);
            _launcher = null;
        }

        if (started) ReleasePause();

        ClearGates();
        if (_marker != null) _marker.Destroy();
        if (_markerA != null) _markerA.Destroy();
        if (_root != null) Destroy(_root.gameObject);
        if (_icon != null) Destroy(_icon);

        if (!started) return;
        _settingsDirty = false;
        _settings.Save();
    }

    private void Update()
    {
        if (_duplicate) return;
        EnsureLauncher();
        FlushSettingsIfDue();

        if (!_rebinding && _settings.ToggleKey != KeyCode.None
            && GUIUtility.keyboardControl == 0
            && Input.GetKeyDown(_settings.ToggleKey))
        {
            _settings.ShowGates = !_settings.ShowGates;
            MarkSettingsDirty();
        }

        HandleWorldInput();

        if (_editorOpen && _draft != null)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                _pickTarget = 0;
                if (_dragMode != 0)
                {
                    _dragMode = 0;
                    _dragTarget = 0;
                }
            }
            if (GUIUtility.keyboardControl == 0
                && Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
            {
                _draft.Undo();
                SyncPointAFromDraft();
                Dbg.Line("undo via keyboard");
            }
        }

        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null)
        {
            ClearGates();
            if (_marker != null) _marker.SetVisible(false);
            return;
        }

        if (vessel.mainBody != null && vessel.mainBody.bodyName != _bodyName) RefreshBodyRoutes();
        if (_settings.ShowWindow) RefreshDistances(vessel);

        if (_smokeStep >= 0 && Time.unscaledTime >= _smokeNext) RunSmoke();

        if (Time.unscaledTime >= _nextReadout)
        {
            _nextReadout = Time.unscaledTime + 0.2f;
            UpdateReadout(vessel);
        }

        if (!_editorOpen && _settings.AutoRoute && Time.unscaledTime >= _nextAutoSelect)
        {
            _nextAutoSelect = Time.unscaledTime + 1f;
            PickAutoRoute(vessel);
        }

        var route = DisplayRoute(vessel);
        if (route == null)
        {
            ClearGates();
            if (_marker != null) _marker.SetVisible(false);
            return;
        }

        UpdateMarker(route, vessel);
        UpdateHover(vessel, FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null);

        if (!_settings.ShowGates || (_settings.HideWhenLanded && vessel.LandedOrSplashed))
        {
            ClearGates();
            return;
        }

        var path = PathFor(route, vessel);
        if (path == null)
        {
            ClearGates();
            return;
        }

        EnsureGates(route);
        if (_gates.Count == 0) return;
        UpdateGates(route, path, vessel);
    }

    private void EnsureLauncher()
    {
        if (_launcher != null || _launcherFailed || ApplicationLauncher.Instance == null) return;

        _icon ??= BuildIcon();
        _launcher = ApplicationLauncher.Instance.AddModApplication(
            () => _settings.ShowWindow = true,
            () => _settings.ShowWindow = false,
            null, null, null, null,
            ApplicationLauncher.AppScenes.FLIGHT,
            _icon);

        if (_launcher == null)
        {
            _launcherFailed = true;
            Debug.LogError("[Glissade3D] launcher button could not be added");
            return;
        }

        Debug.Log("[Glissade3D] launcher button added");
    }

    private void RefreshBodyRoutes()
    {
        var vessel = FlightGlobals.ActiveVessel;
        _bodyName = vessel?.mainBody?.bodyName ?? "";
        _bodyRoutes.Clear();
        _bodyRoutes.AddRange(RouteLibrary.ForBody(_bodyName));
        Debug.Log($"[Glissade3D] body {_bodyName}: {_bodyRoutes.Count} routes");

        if (_bodyRoutes.Count == 0)
        {
            _settings.ActiveRouteId = "";
            return;
        }

        if (_settings.AutoRoute || RouteStore.Find(_bodyRoutes, _settings.ActiveRouteId) == null)
        {
            _settings.AutoRoute = true;
            if (vessel != null) PickAutoRoute(vessel);
        }
    }

    private void PickAutoRoute(Vessel vessel)
    {
        var body = vessel.mainBody;
        if (body == null) return;

        var world = vessel.GetWorldPos3D();
        var best = -1;
        var bestScore = double.MaxValue;

        for (var i = 0; i < _bodyRoutes.Count; i++)
        {
            var route = _bodyRoutes[i];
            var anchor = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
            var distance = (anchor - world).magnitude;
            var bearing = RouteMath.BearingDeg(vessel.latitude, vessel.longitude, route.Lat, route.Lon);
            var alignment = Math.Abs((bearing - route.HeadingDeg + 540.0) % 360.0 - 180.0);
            var score = alignment < 60.0 ? distance : distance + 100000.0;

            if (score >= bestScore) continue;
            bestScore = score;
            best = i;
        }

        if (best < 0) return;

        var chosen = _bodyRoutes[best];
        if (chosen.Id == _settings.ActiveRouteId) return;
        Dbg.Line($"auto route: '{_settings.ActiveRouteId}' -> '{chosen.Id}' ({chosen.DisplayName})");
        _settings.ActiveRouteId = chosen.Id;
    }

    private Route ActiveRoute(Vessel vessel)
    {
        if (vessel == null || vessel.mainBody == null) return null;
        var route = RouteStore.Find(_bodyRoutes, _settings.ActiveRouteId);
        if (route == null) return null;
        if (!string.Equals(route.Body, vessel.mainBody.bodyName, StringComparison.OrdinalIgnoreCase)) return null;
        return route;
    }

    /// <summary>What the world shows: the draft while the editor is open.</summary>
    private Route DisplayRoute(Vessel vessel)
    {
        if (_editorOpen && _draft != null && _draft.Draft != null) return _draft.Draft;
        return ActiveRoute(vessel);
    }

    private void EnsureGates(Route route)
    {
        var count = RouteMath.GateCount(route);
        var key = route.Id + "|" + count;

        if (key == _builtKey && _gates.Count > 0) return;
        RebuildGates(route, key);
    }

    private void RebuildGates(Route route, string key)
    {
        var previousCount = _gates.Count;
        ClearGates();
        _builtKey = key;
        Dbg.Line($"rebuild gates: id={route.Id} count={RouteMath.GateCount(route)} angle={route.EffectiveAngle(_settings.DefaultAngle):0.###} " +
                 $"min={route.MinDistance:0.#} max={route.MaxDistance:0.#} spc={route.Spacing:0.#} " +
                 $"w={route.Width:0.#} h={route.Height:0.#} ew={route.EndWidth:0.#} eh={route.EndHeight:0.#} taper={route.TaperDistance:0.#}");

        var count = RouteMath.GateCount(route);
        for (var i = 0; i < count; i++)
        {
            var gate = new GateVisual(_root);
            gate.SetColor(_editorOpen ? EditingColor : ActiveColor);
            _gates.Add(gate);
        }

        _loggedGeometry = false;
        if (count != previousCount) Debug.Log($"[Glissade3D] gates rebuilt: {count}");
    }

    private void UpdateGates(Route route, ApproachPath path, Vessel vessel)
    {
        var vesselWorldD = vessel.GetWorldPos3D();
        var vesselWorld = RoutePath.VecFrom(vesselWorldD);
        var vesselRender = vessel.transform.position;
        var deviation = path.Deviate(vesselWorld);

        if (!_loggedGeometry)
        {
            _loggedGeometry = true;
            var dme = (vesselWorldD - RoutePath.ToVector3d(path.Origin)).magnitude;
            Debug.Log($"[Glissade3D] geometry: route={route.DisplayName} dme={dme:0} along={deviation.Along:0} lateral={deviation.Lateral:0.0} vertical={deviation.Vertical:0.0} gates={_gates.Count} angle={path.AngleDeg:0.##}");
        }

        var color = _editorOpen ? EditingColor : ActiveColor;
        for (var i = 0; i < _gates.Count; i++)
        {
            var slant = RouteMath.SlantOf(route, i);
            var gate = _gates[i];
            RouteMath.SizeAt(route, slant, out var width, out var height);
            gate.SetSize(width, height);
            gate.SetColor(color);

            var frame = path.GateAt(slant);
            var world = RoutePath.ToVector3d(frame.Center);
            var position = WorldPicker.ToRender(vesselRender, vesselWorldD, world);

            if (i == 0 && Dbg.Enabled && Time.unscaledTime >= _nextAnchorLog)
            {
                _nextAnchorLog = Time.unscaledTime + 1f;
                Dbg.Line($"anchor: frame={Time.frameCount} route=({route.Lat:0.######},{route.Lon:0.######},{route.AltMsl:0.0}) " +
                         $"vesselWorld=({vesselWorldD.x:0.0},{vesselWorldD.y:0.0},{vesselWorldD.z:0.0}) " +
                         $"vesselRender=({vesselRender.x:0.0},{vesselRender.y:0.0},{vesselRender.z:0.0}) " +
                         $"gate0=({world.x:0.0},{world.y:0.0},{world.z:0.0}) " +
                         $"gate0render=({position.x:0.0},{position.y:0.0},{position.z:0.0})");
            }
            var rotation = Quaternion.LookRotation(
                new Vector3((float)frame.Forward.X, (float)frame.Forward.Y, (float)frame.Forward.Z),
                new Vector3((float)frame.Up.X, (float)frame.Up.Y, (float)frame.Up.Z));

            gate.SetActive(true);
            gate.SetTransform(position, rotation);
        }
    }

    private void ClearGates()
    {
        foreach (var gate in _gates) gate.Destroy();
        _gates.Clear();
        _builtKey = "";
    }

    private void UpdateMarker(Route route, Vessel vessel)
    {
        if (_marker == null) return;
        var body = vessel.mainBody;
        if (body == null || !_editorOpen)
        {
            _marker.SetVisible(false);
            return;
        }

        if (double.IsNaN(route.Lat) || double.IsNaN(route.Lon) || double.IsNaN(route.AltMsl))
        {
            _marker.SetVisible(false);
            Dbg.Line($"marker B hidden: bad coordinates {route.Lat} {route.Lon} {route.AltMsl}");
            return;
        }

        var point = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
        var ground = WorldPicker.GroundPoint(body, route.Lat, route.Lon);
        var up = (point - body.position).normalized;
        var distance = (vessel.GetWorldPos3D() - point).magnitude;
        var camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
        var fov = camera != null ? camera.fieldOfView : 60f;

        var terrain = body.TerrainAltitude(route.Lat, route.Lon, true);
        var underground = route.AltMsl < terrain;
        if (underground != _markerUnderground)
        {
            _markerUnderground = underground;
            if (Time.unscaledTime >= _nextMarkerLog)
            {
                _nextMarkerLog = Time.unscaledTime + 2f;
                Dbg.Line($"marker B below terrain={underground} alt={route.AltMsl:0.0} terrain={terrain:0.0}");
            }
        }

        _marker.SetColor(EditingColor);
        _marker.SetVisible(true);
        _marker.Update(vessel.transform.position, vessel.GetWorldPos3D(), point, up, ground, distance, fov, Screen.height);

        if (_markerA == null) return;
        if (!_pointMode || !_hasA)
        {
            _markerA.SetVisible(false);
            return;
        }

        var aPoint = body.GetWorldSurfacePosition(_aLat, _aLon, _aAlt);
        var aGround = WorldPicker.GroundPoint(body, _aLat, _aLon);
        var aUp = (aPoint - body.position).normalized;
        var aDistance = (vessel.GetWorldPos3D() - aPoint).magnitude;
        _markerA.SetColor(PointAColor);
        _markerA.SetVisible(true);
        _markerA.Update(vessel.transform.position, vessel.GetWorldPos3D(), aPoint, aUp, aGround, aDistance, fov, Screen.height);
    }

    private void UpdateHover(Vessel vessel, Camera camera)
    {
        if (!_editorOpen || _draft == null || camera == null)
        {
            if (_marker != null) _marker.SetHighlight(false);
            if (_markerA != null) _markerA.SetHighlight(false);
            return;
        }

        var body = vessel.mainBody;
        var route = _draft.Draft;
        var mouse = (Vector2)Input.mousePosition;

        var bPoint = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
        var bGround = WorldPicker.GroundPoint(body, route.Lat, route.Lon);
        var bHit = Mathf.Min(
            Vector2.Distance(mouse, (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, bPoint))),
            Vector2.Distance(mouse, (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, bGround)))) <= GrabPixels;
        _marker.SetHighlight(bHit && _dragMode == 0);

        if (_markerA == null) return;
        if (!_pointMode || !_hasA)
        {
            _markerA.SetHighlight(false);
            return;
        }

        var aPoint = body.GetWorldSurfacePosition(_aLat, _aLon, _aAlt);
        var aGround = WorldPicker.GroundPoint(body, _aLat, _aLon);
        var aHit = Mathf.Min(
            Vector2.Distance(mouse, (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, aPoint))),
            Vector2.Distance(mouse, (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, aGround)))) <= GrabPixels;
        _markerA.SetHighlight(aHit && _dragMode == 0);
    }

    private void UpdateReadout(Vessel vessel)
    {
        var route = DisplayRoute(vessel);
        if (route == null)
        {
            _readout = Loc.Get("no_route");
            return;
        }

        var path = PathFor(route, vessel);
        if (path == null)
        {
            _readout = Loc.Get("no_geometry");
            return;
        }

        var world = vessel.GetWorldPos3D();
        var deviation = path.Deviate(RoutePath.VecFrom(world));
        var dme = (world - RoutePath.ToVector3d(path.Origin)).magnitude;

        var upVector = RoutePath.ToVector3d(path.Up);
        var velocity = vessel.srf_velocity;
        var verticalSpeed = Vector3d.Dot(velocity, upVector);
        var horizontal = (velocity - upVector * verticalSpeed).magnitude;
        var requiredVertical = -horizontal * path.Tan;

        _readout = string.Join("\n",
            $"{route.DisplayName}  {Loc.Get("heading")} {route.HeadingDeg:0.#}°  {Loc.Get("angle")} {path.AngleDeg:0.0}°",
            $"DME {dme / 1000.0:0.00} km",
            $"LAT {deviation.Lateral:0.0} m   VERT {deviation.Vertical:0.0} m",
            $"VS {Loc.Get("vs_now")} {verticalSpeed:0.0} m/s, {Loc.Get("need")} {requiredVertical:0.0} m/s");
    }

    /// <summary>
    /// The path is world-space, and KSP shifts the world frame under the vessel
    /// (Krakensbane) as it moves. Reusing it across frames leaves the gates
    /// behind in the old frame, so the cache lives for one frame only; Update
    /// and the readout still share it.
    /// </summary>
    private ApproachPath PathFor(Route route, Vessel vessel)
    {
        if (route == null || vessel == null || vessel.mainBody == null) return null;

        var angle = route.EffectiveAngle(_settings.DefaultAngle);
        if (_path != null
            && _pathFrame == Time.frameCount
            && _pathBody == vessel.mainBody
            && _pathId == route.Id
            && _pathLat.Equals(route.Lat)
            && _pathLon.Equals(route.Lon)
            && _pathAlt.Equals(route.AltMsl)
            && _pathHeading.Equals(route.HeadingDeg)
            && _pathAngle.Equals(angle))
        {
            return _path;
        }

        _path = RoutePath.Build(route, vessel.mainBody, _settings.DefaultAngle);
        _pathFrame = Time.frameCount;
        _pathId = route.Id;
        _pathBody = vessel.mainBody;
        _pathLat = route.Lat;
        _pathLon = route.Lon;
        _pathAlt = route.AltMsl;
        _pathHeading = route.HeadingDeg;
        _pathAngle = angle;
        return _path;
    }

    /// <summary>Route distances for the list, refreshed at most once a second.</summary>
    private void RefreshDistances(Vessel vessel)
    {
        if (vessel == null || Time.unscaledTime < _nextDistanceRefresh) return;
        _nextDistanceRefresh = Time.unscaledTime + 1f;
        _routeDistance.Clear();

        var body = vessel.mainBody;
        if (body == null) return;

        var world = vessel.GetWorldPos3D();
        foreach (var route in _bodyRoutes)
        {
            var anchor = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
            _routeDistance[route.Id] = (anchor - world).magnitude;
        }
    }

    /// <summary>
    /// Validation plus terrain sampling for the editor. Terrain probes are
    /// expensive, so the result is cached per draft revision and body.
    /// </summary>
    private List<RouteProblem> EditorProblems()
    {
        var draft = _draft;
        if (draft == null) return _problems;

        var body = VesselBody();
        var bodyName = body != null ? body.bodyName : "";
        var angle = _settings.DefaultAngle;
        if (_problemsRevision == draft.Revision && _problemsBody == bodyName && _problemsAngle.Equals(angle))
            return _problems;

        _problemsRevision = draft.Revision;
        _problemsBody = bodyName;
        _problemsAngle = angle;
        _problems.Clear();
        try
        {
            RouteMath.Validate(draft.Draft, _problems);
            if (body != null) RoutePath.AddTerrainWarnings(draft.Draft, body, angle, _problems);
            _problemsRevision = draft.Revision;
            _problemsBody = bodyName;
            _problemsAngle = angle;
        }
        catch (Exception e)
        {
            _problemsRevision = -1;
            _problems.Clear();
            _problems.Add(new RouteProblem(true, "no_geometry"));
            if (Time.unscaledTime >= _problemsLogAt)
            {
                _problemsLogAt = Time.unscaledTime + 2f;
                Debug.LogError($"[Glissade3D] validation failed: {e.Message}");
            }
        }
        return _problems;
    }

    private void MarkSettingsDirty()
    {
        _settingsDirty = true;
        _settingsSaveAt = Time.unscaledTime + 0.75f;
    }

    private void FlushSettingsIfDue()
    {
        if (!_settingsDirty || Time.unscaledTime < _settingsSaveAt) return;
        _settingsDirty = false;
        _settings.Save();
    }

    // ---------------------------------------------------------------- editor

    private void OpenEditor(Route route, bool isNew)
    {
        if (_editorOpen)
        {
            if (_draft != null && _draft.IsDirty)
            {
                Toast(Loc.Get("close_editor_first"));
                return;
            }
            CloseEditor();
        }

        _draft = new RouteDraft(route, isNew);
        _pressId = "";
        _editorOpen = true;
        _pickTarget = 0;
        _pointMode = false;
        _hasA = false;
        _dragMode = 0;
        _confirmDiscard = false;
        _confirmDelete = false;
        _pendingDeleteId = "";
        _buffers.Clear();
        _problemsRevision = -1;
        _problems.Clear();
        Debug.Log($"[Glissade3D] editor opened: {route.DisplayName} new={isNew} readOnly={route.BuiltIn}");
        Dbg.Line($"editor fields: id={route.Id} lat={route.Lat:0.########} lon={route.Lon:0.########} alt={route.AltMsl:0.##} " +
                 $"hdg={route.HeadingDeg:0.###} ang={route.AngleDeg:0.###} def={route.AngleFromDefault} " +
                 $"min={route.MinDistance:0.#} max={route.MaxDistance:0.#} spc={route.Spacing:0.#} " +
                 $"w={route.Width:0.#} h={route.Height:0.#} ew={route.EndWidth:0.#} eh={route.EndHeight:0.#} taper={route.TaperDistance:0.#}");
    }

    private void CloseEditor()
    {
        if (_draft != null) Dbg.Line($"close editor: dirty={_draft.IsDirty} undo={_draft.UndoCount}");
        ReleasePause();
        _editorOpen = false;
        _draft = null;
        _pickTarget = 0;
        _dragMode = 0;
        _confirmDiscard = false;
        _buffers.Clear();
        if (_marker != null) _marker.SetVisible(false);
        if (_markerA != null) _markerA.SetVisible(false);
        ClearGates();
    }

    private void ApplyDraft()
    {
        if (_draft == null) return;

        var problems = EditorProblems();
        if (Dbg.Enabled)
        {
            Dbg.Line($"validate: {problems.Count} problem(s)");
            foreach (var problem in problems)
            {
                Dbg.Line($"  {(problem.IsError ? "error" : "warning")}: {problem.Key}");
            }
        }
        if (RouteMath.HasErrors(problems))
        {
            foreach (var problem in problems)
            {
                if (!problem.IsError) continue;
                _errorText = Loc.Get(problem.Key);
                break;
            }
            Debug.LogWarning("[Glissade3D] apply blocked: validation errors");
            return;
        }

        if (!RouteLibrary.UpsertUser(_draft.Draft, out var error))
        {
            _errorText = Loc.Get("save_failed") + ": " + error;
            return;
        }

        _settings.ActiveRouteId = _draft.Draft.Id;
        _settings.AutoRoute = false;
        MarkSettingsDirty();
        _errorText = "";
        Toast(Loc.Get("saved"));
        Debug.Log($"[Glissade3D] route applied: {_draft.Draft.DisplayName} id={_draft.Draft.Id}");
        Dbg.Line($"applied: {_draft.Draft.DisplayName} id={_draft.Draft.Id}");
        CloseEditor();
        RefreshBodyRoutes();
    }

    private void DeletePending()
    {
        var id = _pendingDeleteId;
        _pendingDeleteId = "";
        _confirmDelete = false;

        var route = RouteStore.Find(_bodyRoutes, id);
        Dbg.Line($"delete route: {route?.Id} builtIn={route?.BuiltIn}");
        if (route == null || route.BuiltIn) return;
        if (!RouteLibrary.DeleteUser(route.Id, out var error))
        {
            _errorText = Loc.Get("save_failed") + ": " + error;
            return;
        }

        if (_settings.ActiveRouteId == id)
        {
            _settings.ActiveRouteId = "";
            _settings.AutoRoute = true;
        }
        MarkSettingsDirty();
        CloseEditor();
        RefreshBodyRoutes();
        Toast(Loc.Get("saved"));
    }

    private void Toast(string text)
    {
        _toast = text;
        _toastUntil = Time.unscaledTime + 3f;
    }

    private void HandleWorldInput()
    {
        if (!_editorOpen || _draft == null) return;

        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;
        var camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
        if (camera == null) return;

        if (Input.GetMouseButtonUp(0))
        {
            if (_dragMode != 0)
            {
                Dbg.Line($"drag end target={_dragTarget} mode={_dragMode} a={_aLat:0.########},{_aLon:0.########},{_aAlt:0.##} b={_draft.Draft.Lat:0.########},{_draft.Draft.Lon:0.########},{_draft.Draft.AltMsl:0.##}");
                Debug.Log("[Glissade3D] drag ended");
            }
            _dragMode = 0;
            _dragTarget = 0;
            return;
        }

        if (_dragMode != 0)
        {
            if (Input.GetMouseButton(0)) ContinueDrag(camera);
            return;
        }

        if (MouseOverWindows()) return;

        if (_pickTarget != 0 && Input.GetMouseButtonDown(0))
        {
            PlacePoint(camera);
            return;
        }

        if (Input.GetMouseButtonDown(0)) BeginDrag(camera);
    }

    private bool BeginDrag(Camera camera)
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return false;
        var body = vessel.mainBody;
        var route = _draft.Draft;
        var mouse = (Vector2)Input.mousePosition;

        var best = GrabPixels;
        var target = 0;
        var mode = 0;

        ConsiderHandle(vessel, camera, mouse,
            body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl),
            WorldPicker.GroundPoint(body, route.Lat, route.Lon), 1, ref best, ref target, ref mode);

        if (_pointMode && _hasA)
        {
            ConsiderHandle(vessel, camera, mouse,
                body.GetWorldSurfacePosition(_aLat, _aLon, _aAlt),
                WorldPicker.GroundPoint(body, _aLat, _aLon), 2, ref best, ref target, ref mode);
        }

        if (target == 0) return false;

        _dragTarget = target;
        _dragMode = mode;

        var planePoint = target == 2
            ? WorldPicker.GroundPoint(body, _aLat, _aLon)
            : WorldPicker.GroundPoint(body, route.Lat, route.Lon);
        _dragPlanePoint = planePoint;
        _dragPlaneNormal = (planePoint - body.position).normalized;

        Change(target == 2 ? "point A" : "move point");
        Dbg.Line($"drag begin target={_dragTarget} mode={_dragMode} distance={best:0.#}px");
        Debug.Log($"[Glissade3D] drag begin target={_dragTarget} mode={_dragMode}");
        return true;
    }

    private static void ConsiderHandle(Vessel vessel, Camera camera, Vector2 mouse,
        Vector3d point, Vector3d ground, int target, ref float best, ref int bestTarget, ref int bestMode)
    {
        var pointScreen = (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, point));
        var groundScreen = (Vector2)camera.WorldToScreenPoint(RenderPosition(vessel, ground));

        var toPoint = Vector2.Distance(mouse, pointScreen);
        if (toPoint <= best)
        {
            best = toPoint;
            bestTarget = target;
            bestMode = 2;
        }

        var toGround = Vector2.Distance(mouse, groundScreen);
        if (toGround <= best)
        {
            best = toGround;
            bestTarget = target;
            bestMode = 1;
        }
    }

    private void ContinueDrag(Camera camera)
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;
        var body = vessel.mainBody;
        var mouse = (Vector2)Input.mousePosition;

        if (_dragMode == 1)
        {
            if (!WorldPicker.TryGroundMove(body, camera, mouse, _dragPlanePoint, _dragPlaneNormal, out var moved)) return;
            if ((moved - vessel.GetWorldPos3D()).magnitude > 300000.0) return;

            WorldPicker.ToLatLonAlt(body, moved, out var lat, out var lon, out _);
            ApplyDrag(lat, lon, null);
        }
        else if (_dragMode == 2)
        {
            var point = CurrentDragPoint(body);
            if (!WorldPicker.TryVerticalMove(body, camera, mouse, point, out var moved)) return;

            var lat = body.GetLatitude(moved);
            var lon = body.GetLongitude(moved);
            var terrain = body.TerrainAltitude(lat, lon, true);
            ApplyDrag(null, null, Math.Max(body.GetAltitude(moved), terrain + 0.5));
        }
    }

    private Vector3d CurrentDragPoint(CelestialBody body)
    {
        if (_dragTarget == 2) return body.GetWorldSurfacePosition(_aLat, _aLon, _aAlt);
        var route = _draft.Draft;
        return body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
    }

    private void ApplyDrag(double? lat, double? lon, double? alt)
    {
        if (_draft == null) return;

        if (_dragTarget == 2)
        {
            if (lat.HasValue) _aLat = lat.Value;
            if (lon.HasValue) _aLon = lon.Value;
            if (alt.HasValue) _aAlt = alt.Value;
            ApplyTwoPointsLive();
            return;
        }

        var route = _draft.Draft;
        if (lat.HasValue) route.Lat = lat.Value;
        if (lon.HasValue) route.Lon = RoutePath.NormalizeLon(lon.Value);
        if (alt.HasValue) route.AltMsl = alt.Value;
        ApplyTwoPointsLive();
    }

    private void PlacePoint(Camera camera)
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null || _draft == null) return;

        var body = vessel.mainBody;
        var route = _draft.Draft;
        var current = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);

        if (!WorldPicker.TryPick(camera, current, out var world)) return;
        WorldPicker.ToLatLonAlt(body, world, out var lat, out var lon, out var alt);

        var target = _pickTarget;
        _pickTarget = 0;

        if (target == 2)
        {
            Change("point A");
            _aLat = lat;
            _aLon = lon;
            _aAlt = alt;
            _hasA = true;
            ApplyTwoPointsLive();
            Toast($"{Loc.Get("point_a")}: {lat:0.00000}, {lon:0.00000}, {alt:0.0}");
            Dbg.Line($"point A picked {lat:0.00000} {lon:0.00000} {alt:0.0}");
            return;
        }

        Change("place point");
        route.Lat = lat;
        route.Lon = lon;
        route.AltMsl = alt;
        ApplyTwoPointsLive();
        Toast($"{lat:0.00000}, {lon:0.00000}, {alt:0.0}");
        Dbg.Line($"point {(target == 3 ? "B" : "")} picked {lat:0.00000} {lon:0.00000} {alt:0.0}");
        Debug.Log($"[Glissade3D] point placed {lat:0.00000} {lon:0.00000} {alt:0.0}");
    }

    private void Change(string label)
    {
        if (_draft == null) return;
        if (_draft.CanUndo && _draft.LastLabel == label) return;
        _draft.BeginChange(label);
        var route = _draft.Draft;
        Dbg.Line($"change '{label}': lat={route.Lat:0.########} lon={route.Lon:0.########} alt={route.AltMsl:0.##} " +
                 $"hdg={route.HeadingDeg:0.###} ang={route.AngleDeg:0.###} min={route.MinDistance:0.#} " +
                 $"max={route.MaxDistance:0.#} spc={route.Spacing:0.#} w={route.Width:0.#} h={route.Height:0.#} " +
                 $"ew={route.EndWidth:0.#} eh={route.EndHeight:0.#} taper={route.TaperDistance:0.#}");
    }

    private static Vector3 RenderPosition(Vessel vessel, Vector3d world)
    {
        return WorldPicker.ToRender(vessel.transform.position, vessel.GetWorldPos3D(), world);
    }

    private bool MouseOverWindows()
    {
        var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (_settings.ShowWindow && _mainRect.width > 1f && _mainRect.Contains(mouse)) return true;
        if (_editorOpen && _editorRect.width > 1f && _editorRect.Contains(mouse)) return true;
        return false;
    }

    /// <summary>Keeps both windows reachable after a resolution or UI-scale change.</summary>
    private void ClampWindows()
    {
        if (Screen.width < 100 || Screen.height < 100) return;

        _settings.WindowX = Mathf.Clamp(_settings.WindowX, 0f, Mathf.Max(0f, Screen.width - 140f));
        _settings.WindowY = Mathf.Clamp(_settings.WindowY, 0f, Mathf.Max(0f, Screen.height - 60f));
        _settings.EditorX = Mathf.Clamp(_settings.EditorX, 0f, Mathf.Max(0f, Screen.width - 140f));
        _settings.EditorY = Mathf.Clamp(_settings.EditorY, 0f, Mathf.Max(0f, Screen.height - 60f));
    }

    private void DrawWorldLabels()
    {
        if (!_editorOpen || _draft == null) return;

        var vessel = FlightGlobals.ActiveVessel;
        var camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
        if (vessel == null || camera == null) return;

        var body = vessel.mainBody;
        var route = _draft.Draft;
        DrawPointLabel(camera, vessel, body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl), "B", EditingColor);

        if (_pointMode && _hasA)
        {
            DrawPointLabel(camera, vessel, body.GetWorldSurfacePosition(_aLat, _aLon, _aAlt), "A", PointAColor);
        }
    }

    private static void DrawPointLabel(Camera camera, Vessel vessel, Vector3d world, string text, Color color)
    {
        var screen = camera.WorldToScreenPoint(RenderPosition(vessel, world));
        if (screen.z <= 0f) return;

        var old = GUI.color;
        GUI.color = color;
        GUI.Label(new Rect(screen.x + 12f, Screen.height - screen.y - 12f, 30f, 24f), text);
        GUI.color = old;
    }

    // ------------------------------------------------------------------ pause

    private void TogglePause()
    {
        if (_pausedByUs)
        {
            ReleasePause();
            return;
        }

        _savedTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
        Time.timeScale = 0f;
        _pausedByUs = true;
        InputLockManager.SetControlLock(
            ControlTypes.STAGING | ControlTypes.VESSEL_SWITCHING | ControlTypes.MAP_TOGGLE,
            "Glissade3DEditor");
        Debug.Log("[Glissade3D] editor pause on");
    }

    private void ReleasePause()
    {
        if (!_pausedByUs) return;
        _pausedByUs = false;
        InputLockManager.RemoveControlLock("Glissade3DEditor");
        if (Math.Abs(Time.timeScale) < 0.001f) Time.timeScale = _savedTimeScale > 0f ? _savedTimeScale : 1f;
        Debug.Log("[Glissade3D] editor pause off");
    }

    // -------------------------------------------------------------------- gui

    private void OnGUI()
    {
        if (_duplicate) return;
        if (_rebinding && Event.current != null && Event.current.type == EventType.KeyDown)
        {
            var key = Event.current.keyCode;
            if (key == KeyCode.Escape) _rebinding = false;
            else if (key != KeyCode.None
                     && key != KeyCode.LeftAlt && key != KeyCode.RightAlt
                     && key != KeyCode.LeftControl && key != KeyCode.RightControl
                     && key != KeyCode.LeftShift && key != KeyCode.RightShift)
            {
                _settings.ToggleKey = key;
                _rebinding = false;
                MarkSettingsDirty();
            }
            Event.current.Use();
            return;
        }

        if (!_settings.ShowWindow && !_editorOpen) return;
        if (Event.current != null && Event.current.type == EventType.Layout) ClampWindows();

        if (_settings.ShowWindow)
        {
            var rect = new Rect(_settings.WindowX, _settings.WindowY, _settings.WindowW, _settings.WindowH);
            var result = GUI.Window(WindowId, rect, DrawMainWindow, "Glissade 3D");
            _settings.WindowX = result.x;
            _settings.WindowY = result.y;
            if (result.width > 1f) _mainRect = result;
        }

        if (_editorOpen && _draft != null)
        {
            var rect = new Rect(_settings.EditorX, _settings.EditorY, _settings.EditorW, _settings.EditorH);
            var result = GUI.Window(EditorId, rect, DrawEditorWindow, Loc.Get("edit"));
            _settings.EditorX = result.x;
            _settings.EditorY = result.y;
            if (result.width > 1f) _editorRect = result;
        }

        DrawWorldLabels();
    }

    private void DrawMainWindow(int id)
    {
        var vessel = FlightGlobals.ActiveVessel;
        var route = vessel != null ? ActiveRoute(vessel) : null;
        var routeName = route != null ? route.DisplayName : Loc.Get("none");
        var modeName = _settings.AutoRoute ? Loc.Get("auto") : Loc.Get("manual");

        _contentScroll = GUILayout.BeginScrollView(_contentScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
        GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(280f, _settings.WindowW - 56f)));

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(_pausedByUs ? Loc.Get("paused") : Loc.Get("pause"), GUILayout.Width(140f))) TogglePause();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Label($"{Loc.Get("body")}: {_bodyName}   {Loc.Get("route")}: {routeName} ({modeName})");

        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.Get("search"), GUILayout.Width(60f));
        _search = GUILayout.TextField(_search ?? "");
        if (GUILayout.Button(Loc.Get("new_route"), GUILayout.Width(120f))) CreateRoute();
        GUILayout.EndHorizontal();

        _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(150f));
        foreach (var candidate in _bodyRoutes)
        {
            if (!Matches(candidate, _search)) continue;
            var selected = candidate.Id == _settings.ActiveRouteId;
            var distance = _routeDistance.TryGetValue(candidate.Id, out var cached) ? cached : 0.0;
            var label = $"{(selected ? "> " : "   ")}{candidate.DisplayName}  {candidate.HeadingDeg:0.#}°  {distance / 1000.0:0.0} km{(_sharedState.Contains(candidate.Id) ? "  [S]" : "")}{(candidate.BuiltIn ? "  *" : "")}";

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(label))
            {
                if (Event.current != null && Event.current.clickCount >= 2 && !candidate.BuiltIn) OpenEditor(candidate, false);
                else Select(candidate.Id);
            }

            GUI.enabled = !candidate.BuiltIn;
            if (GUILayout.Button(Loc.Get("edit_short"), GUILayout.Width(64f))) OpenEditor(candidate, false);
            GUI.enabled = true;

            if (GUILayout.Button(Loc.Get("dup_short"), GUILayout.Width(64f))) DuplicateRoute(candidate);

            GUI.enabled = !candidate.BuiltIn;
            if (GUILayout.Button(Loc.Get("del_short"), GUILayout.Width(56f)))
            {
                _pendingDeleteId = candidate.Id;
                _confirmDelete = true;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        if (GUILayout.Button(Loc.Get("auto_nearest"))) SetAuto();

        var show = GUILayout.Toggle(_settings.ShowGates, Loc.Get("show_gates"));
        if (show != _settings.ShowGates)
        {
            _settings.ShowGates = show;
            MarkSettingsDirty();
        }

        GUILayout.Label($"{Loc.Get("glideslope")} {_settings.DefaultAngle:0.0}°  ({Loc.Get("read_only")})");
        var nudge = Event.current != null && Event.current.shift ? 1.0 : 0.1;
        GUILayout.BeginHorizontal();
        var angle = GUILayout.HorizontalSlider((float)_settings.DefaultAngle,
            (float)RouteMath.MinAngle, (float)RouteMath.MaxAngle);
        angle = Mathf.Round(angle * 10f) / 10f;
        if (RepeatButton("main-angle-", "-", 24f)) angle -= (float)nudge;
        if (RepeatButton("main-angle+", "+", 24f)) angle += (float)nudge;
        GUILayout.EndHorizontal();
        angle = Mathf.Clamp(angle, (float)RouteMath.MinAngle, (float)RouteMath.MaxAngle);
        if (Math.Abs(angle - _settings.DefaultAngle) > 1e-4)
        {
            _settings.DefaultAngle = angle;
            MarkSettingsDirty();
            ClearGates();
        }

        if (GUILayout.Button($"{Loc.Get("advanced")} {(_settings.Advanced ? "▾" : "▸")}"))
        {
            _settings.Advanced = !_settings.Advanced;
            MarkSettingsDirty();
        }

        if (_settings.Advanced)
        {
            var keyLabel = _rebinding ? Loc.Get("press_key") : $"{Loc.Get("gates_key")}: {_settings.ToggleKey}";
            if (GUILayout.Button(keyLabel)) _rebinding = true;

            var hide = GUILayout.Toggle(_settings.HideWhenLanded, Loc.Get("hide_landed"));
            if (hide != _settings.HideWhenLanded)
            {
                _settings.HideWhenLanded = hide;
                MarkSettingsDirty();
            }

            var debug = GUILayout.Toggle(_settings.DebugLog, Loc.Get("debug_log"));
            if (debug != _settings.DebugLog)
            {
                _settings.DebugLog = debug;
                Dbg.Enabled = debug || Environment.GetEnvironmentVariable("GLISSADE_DEBUG") == "1";
                MarkSettingsDirty();
                Dbg.Line("debug logging toggled from window");
            }

            if (GUILayout.Button(Loc.Get("reset")))
            {
                _settings.ResetToDefaults();
                MarkSettingsDirty();
                ClearGates();
            }

            if (GUILayout.Button(Loc.Get("read_routes"))) ReadRoutesFromDisk();

            GUILayout.Space(6);
            GUILayout.Label($"{Loc.Get("shared_routes")}: {_sharedState.Count}");
            GUILayout.BeginHorizontal();
            GUI.enabled = !_sharedBusy && !string.IsNullOrEmpty(SharedBaseUrl());
            if (GUILayout.Button(Loc.Get("install_shared"))) StartCoroutine(InstallSharedRoutine());
            GUI.enabled = !_sharedBusy && _sharedState.Count > 0;
            if (GUILayout.Button(Loc.Get("uninstall_shared")))
            {
                _confirmUninstall = true;
                _confirmDelete = false;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_sharedBusy || !string.IsNullOrEmpty(_sharedStatus)) GUILayout.Label(_sharedStatus);

            if (_confirmUninstall)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.Get("shared_confirm"));
                if (GUILayout.Button(Loc.Get("delete"))) UninstallShared();
                if (GUILayout.Button(Loc.Get("cancel"))) _confirmUninstall = false;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(4);
        GUILayout.Label(_readout);

        if (route != null && route.BuiltIn) GUILayout.Label(Loc.Get("duplicate_to_edit"));

        if (_confirmDelete)
        {
            var pending = RouteStore.Find(_bodyRoutes, _pendingDeleteId);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{Loc.Get("delete_confirm")} {pending?.DisplayName}");
            if (GUILayout.Button(Loc.Get("delete"))) DeletePending();
            if (GUILayout.Button(Loc.Get("cancel")))
            {
                _confirmDelete = false;
                _pendingDeleteId = "";
            }
            GUILayout.EndHorizontal();
        }

        if (_toast.Length > 0 && Time.unscaledTime < _toastUntil) GUILayout.Label(_toast);
        if (_errorText.Length > 0) GUILayout.Label(_errorText);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(Loc.Get("close"), GUILayout.Width(140f))) CloseMainWindow();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(60f, _settings.WindowW - 24f), 20f));
    }

    private void DrawEditorWindow(int id)
    {
        var draft = _draft;
        if (draft == null) return;
        var route = draft.Draft;

        _editorScroll = GUILayout.BeginScrollView(_editorScroll, GUIStyle.none, GUI.skin.verticalScrollbar);
        GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(300f, _settings.EditorW - 56f)));

        GUILayout.Label($"{(draft.IsNew ? Loc.Get("new_route") : Loc.Get("edit"))}: {route.DisplayName}");

        GUILayout.BeginHorizontal();
        GUILayout.Label(Loc.Get("route_name"), GUILayout.Width(110f));
        var name = GUILayout.TextField(route.Name ?? "");
        if (name != route.Name)
        {
            Change("rename");
            route.Name = name;
        }
        GUILayout.EndHorizontal();

        GUILayout.Label($"{Loc.Get("body")}: {route.Body}");

        GUILayout.Space(4);
        GUILayout.Label(Loc.Get("point") + " — " + (_pointMode ? Loc.Get("mode_two_points") : Loc.Get("mode_point_angle")));

        if (_pointMode)
        {
            var radius = VesselBody()?.Radius ?? 600000.0;
            var latStep = (float)(_nudgeMetres / radius * 180.0 / Math.PI);
            var lonStepA = (float)(_nudgeMetres / (radius * Math.Max(Math.Cos(_aLat * Math.PI / 180.0), 1e-6)) * 180.0 / Math.PI);
            var lonStepB = (float)(_nudgeMetres / (radius * Math.Max(Math.Cos(route.Lat * Math.PI / 180.0), 1e-6)) * 180.0 / Math.PI);
            var oldColor = GUI.color;

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(150f));
            GUI.color = PointAColor;
            GUILayout.Label("A");
            GUI.color = oldColor;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.Get("set_here"))) SetPointA();
            if (GUILayout.Button(_pickTarget == 2 ? Loc.Get("picking") : Loc.Get("pick_click"))) _pickTarget = _pickTarget == 2 ? 0 : 2;
            GUILayout.EndHorizontal();
            var aLat = CompactNumberRow(Loc.Get("lat_short"), _aLat, latStep, "0.00000", "aLat");
            var aLon = CompactNumberRow(Loc.Get("lon_short"), _aLon, lonStepA, "0.00000", "aLon");
            var aAlt = CompactNumberRow(Loc.Get("alt_short"), _aAlt, _nudgeMetres, "0.0", "aAlt");
            if (Math.Abs(aLat - _aLat) > 1e-9 || Math.Abs(aLon - _aLon) > 1e-9 || Math.Abs(aAlt - _aAlt) > 1e-6)
            {
                Change("point A");
                _aLat = aLat;
                _aLon = RoutePath.NormalizeLon(aLon);
                _aAlt = aAlt;
                _hasA = true;
                ApplyTwoPointsLive();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(150f));
            GUI.color = EditingColor;
            GUILayout.Label("B");
            GUI.color = oldColor;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.Get("set_here"))) SetPointB();
            if (GUILayout.Button(_pickTarget == 3 ? Loc.Get("picking") : Loc.Get("pick_click"))) _pickTarget = _pickTarget == 3 ? 0 : 3;
            GUILayout.EndHorizontal();
            var bLat = CompactNumberRow(Loc.Get("lat_short"), route.Lat, latStep, "0.00000", "bLat");
            var bLon = CompactNumberRow(Loc.Get("lon_short"), route.Lon, lonStepB, "0.00000", "bLon");
            var bAlt = CompactNumberRow(Loc.Get("alt_short"), route.AltMsl, _nudgeMetres, "0.0", "bAlt");
            if (Math.Abs(bLat - route.Lat) > 1e-9 || Math.Abs(bLon - route.Lon) > 1e-9 || Math.Abs(bAlt - route.AltMsl) > 1e-6)
            {
                Change("point B");
                route.Lat = bLat;
                route.Lon = RoutePath.NormalizeLon(bLon);
                route.AltMsl = bAlt;
                ApplyTwoPointsLive();
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUI.color = oldColor;
        }
        else
        {
            var radius = VesselBody()?.Radius ?? 600000.0;
            var latStep = (float)(_nudgeMetres / radius * 180.0 / Math.PI);
            var lonStep = (float)(_nudgeMetres / (radius * Math.Max(Math.Cos(route.Lat * Math.PI / 180.0), 1e-6)) * 180.0 / Math.PI);

            var lat = NumberRow(Loc.Get("lat"), route.Lat, latStep, "0.00000", "lat");
            var lon = NumberRow(Loc.Get("lon"), route.Lon, lonStep, "0.00000", "lon");
            var alt = NumberRow(Loc.Get("alt"), route.AltMsl, _nudgeMetres, "0.0", "alt");
            if (Math.Abs(lat - route.Lat) > 1e-9) { Change("move point"); route.Lat = lat; }
            if (Math.Abs(lon - route.Lon) > 1e-9) { Change("move point"); route.Lon = RoutePath.NormalizeLon(lon); }
            if (Math.Abs(alt - route.AltMsl) > 1e-6) { Change("move point"); route.AltMsl = alt; }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(_pickTarget == 1 ? Loc.Get("picking") : Loc.Get("pick_click"))) _pickTarget = _pickTarget == 1 ? 0 : 1;
            if (GUILayout.Button(Loc.Get("from_vessel"))) FromVessel();
            if (GUILayout.Button(Loc.Get("drop_ground"))) DropToGround();
            GUILayout.EndHorizontal();

            GUILayout.Label($"{Loc.Get("nudge")}: {_nudgeMetres:0} m — {Loc.Get("hold_hint")}");
        }

        if (GUILayout.Button(Loc.Get("switch_mode")))
        {
            _pointMode = !_pointMode;
            if (_pointMode) SeedPointA();
            else _hasA = false;
            _pickTarget = 0;
            _buffers.Clear();
            GUI.FocusControl("");
            Dbg.Line($"mode switched: twoPoints={_pointMode}");
        }

        if (!_pointMode)
        {
            var headingStep = Event.current != null && Event.current.shift ? 10f : 1f;
            var heading = NumberRow(Loc.Get("heading"), route.HeadingDeg, headingStep, "0.0", "heading");
            if (Math.Abs(heading - route.HeadingDeg) > 1e-6)
            {
                Change("heading");
                route.HeadingDeg = (heading % 360.0 + 360.0) % 360.0;
            }

            var angleStep = Event.current != null && Event.current.shift ? 1f : 0.1f;
            var angleValue = NumberRow(Loc.Get("angle"), route.EffectiveAngle(_settings.DefaultAngle), angleStep, "0.0", "angle");
            var currentAngle = route.EffectiveAngle(_settings.DefaultAngle);
            if (Math.Abs(angleValue - currentAngle) > 1e-6)
            {
                Change("angle");
                route.AngleFromDefault = false;
                route.AngleDeg = Math.Max(RouteMath.MinAngle, Math.Min(RouteMath.MaxAngle, angleValue));
            }
        }

        if (GUILayout.Button($"{Loc.Get("advanced")} {(_advancedProfile ? "▾" : "▸")}")) _advancedProfile = !_advancedProfile;
        if (_advancedProfile)
        {
            ProfileRow("minDistance", Loc.Get("min_distance"), route, r => r.MinDistance, (r, v) => r.MinDistance = v, 10f);
            ProfileRow("maxDistance", Loc.Get("max_distance"), route, r => r.MaxDistance, (r, v) => r.MaxDistance = v, 100f);
            ProfileRow("spacing", Loc.Get("spacing"), route, r => r.Spacing, (r, v) => r.Spacing = v, 10f);
            ProfileRow("width", Loc.Get("width"), route, r => r.Width, (r, v) => r.Width = v, 1f);
            ProfileRow("height", Loc.Get("height"), route, r => r.Height, (r, v) => r.Height = v, 1f);
            ProfileRow("endWidth", Loc.Get("end_size") + " W", route, r => r.EndWidth, (r, v) => r.EndWidth = v, 0.5f);
            ProfileRow("endHeight", Loc.Get("end_size") + " H", route, r => r.EndHeight, (r, v) => r.EndHeight = v, 0.5f);
            ProfileRow("taper", Loc.Get("taper"), route, r => r.TaperDistance, (r, v) => r.TaperDistance = v, 5f);
        }

        GUILayout.Space(6);
        var problems = EditorProblems();
        foreach (var problem in problems)
        {
            var color = GUI.color;
            GUI.color = problem.IsError ? new Color(1f, 0.5f, 0.4f) : new Color(1f, 0.85f, 0.4f);
            GUILayout.Label((problem.IsError ? "! " : "? ") + Loc.Get(problem.Key));
            GUI.color = color;
        }

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        var canApply = !RouteMath.HasErrors(problems);
        GUI.enabled = canApply;
        if (GUILayout.Button(Loc.Get("apply"), GUILayout.Width(110f))) ApplyDraft();
        GUI.enabled = true;
        GUI.enabled = draft.CanUndo;
        if (GUILayout.Button(Loc.Get("undo") + (draft.CanUndo ? ": " + draft.LastLabel : "")))
        {
            draft.Undo();
            SyncPointAFromDraft();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (_errorText.Length > 0) GUILayout.Label(_errorText);
        if (_toast.Length > 0 && Time.unscaledTime < _toastUntil) GUILayout.Label(_toast);

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (_confirmDiscard)
        {
            GUILayout.Label(Loc.Get("discard"));
            if (GUILayout.Button(Loc.Get("discard_yes"))) { CloseEditor(); }
            if (GUILayout.Button(Loc.Get("discard_no"))) _confirmDiscard = false;
        }
        else
        {
            if (GUILayout.Button(Loc.Get("close"))) RequestCloseEditor();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(60f, _settings.EditorW - 24f), 20f));
    }

    private CelestialBody VesselBody()
    {
        var vessel = FlightGlobals.ActiveVessel;
        return vessel != null ? vessel.mainBody : null;
    }

    private void ProfileRow(string id, string label, Route route,
        Func<Route, float> get, Action<Route, float> set, float step)
    {
        var value = NumberRow(label, get(route), step, "0.##", "p_" + id);
        if (Math.Abs(value - get(route)) < 1e-4) return;
        Change(id);
        set(route, (float)Math.Round(value, 3));
    }

    private double NumberRow(string label, double value, float step, string format, string id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(110f));

        var focused = GUI.GetNameOfFocusedControl() == id;
        if (!focused) _buffers.Remove(id);

        var text = focused && _buffers.TryGetValue(id, out var buffer)
            ? buffer
            : value.ToString(format, CultureInfo.InvariantCulture);

        GUI.SetNextControlName(id);
        var typed = GUILayout.TextField(text, GUILayout.Width(110f));
        if (focused && typed != text) _buffers[id] = typed;

        if (focused && _buffers.TryGetValue(id, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
        }

        if (RepeatButton(id + "-", "-", 24f)) value -= step;
        if (RepeatButton(id + "+", "+", 24f)) value += step;
        GUILayout.EndHorizontal();
        return value;
    }

    private bool RepeatButton(string id, string label, float width)
    {
        var pressed = GUILayout.RepeatButton(label, GUILayout.Width(width));
        var now = Time.unscaledTime;

        if (!Input.GetMouseButton(0))
        {
            _pressId = "";
            return false;
        }

        if (!pressed) return false;

        if (_pressId != id)
        {
            _pressId = id;
            _holdAt = now + HoldDelay;
            return true;
        }

        if (now < _holdAt) return false;

        _holdAt = now + HoldRepeat;
        return true;
    }

    private double CompactNumberRow(string label, double value, float step, string format, string id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(22f));

        var focused = GUI.GetNameOfFocusedControl() == id;
        if (!focused) _buffers.Remove(id);

        var text = focused && _buffers.TryGetValue(id, out var buffer)
            ? buffer
            : value.ToString(format, CultureInfo.InvariantCulture);

        GUI.SetNextControlName(id);
        var typed = GUILayout.TextField(text, GUILayout.Width(72f));
        if (focused && typed != text) _buffers[id] = typed;

        if (focused && _buffers.TryGetValue(id, out var raw)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
        }

        if (RepeatButton(id + "-", "-", 20f)) value -= step;
        if (RepeatButton(id + "+", "+", 20f)) value += step;
        GUILayout.EndHorizontal();
        return value;
    }

    // --------------------------------------------------------------- actions

    private void CreateRoute()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;

        var body = vessel.mainBody;
        var lat = vessel.latitude;
        var lon = RoutePath.NormalizeLon(vessel.longitude);
        var alt = body.GetAltitude(vessel.GetWorldPos3D());
        var heading = VesselHeading(vessel);
        var name = RouteStore.UniqueName($"{body.bodyName} route", RouteLibrary.Routes);

        var template = _settings.NewRouteTemplate(body.bodyName, lat, lon, alt, heading, name, Route.NewId());
        Dbg.Line($"new route: {name} lat={lat:0.########} lon={lon:0.########} alt={alt:0.##} hdg={heading:0.###}");
        OpenEditor(template, true);
    }

    private void DuplicateRoute(Route route)
    {
        var copy = RouteStore.Duplicate(route, RouteLibrary.Routes);
        if (copy == null) return;
        OpenEditor(copy, true);
        Debug.Log($"[Glissade3D] duplicated {route.DisplayName} -> {copy.DisplayName}");
    }

    private void FromVessel()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;
        var body = vessel.mainBody;
        Change("move point");
        _draft.Draft.Lat = vessel.latitude;
        _draft.Draft.Lon = RoutePath.NormalizeLon(vessel.longitude);
        _draft.Draft.AltMsl = body.GetAltitude(vessel.GetWorldPos3D());
    }

    private void DropToGround()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;
        var body = vessel.mainBody;
        var route = _draft.Draft;
        Change("move point");
        route.AltMsl = body.TerrainAltitude(route.Lat, route.Lon, true);
    }

    private void SeedPointA()
    {
        if (_draft == null || _hasA) return;

        var body = VesselBody();
        if (body == null) return;

        var path = RoutePath.Build(_draft.Draft, body, _settings.DefaultAngle);
        if (path == null) return;

        var slant = Math.Max((double)_draft.Draft.MaxDistance, _draft.Draft.MinDistance);
        var far = RoutePath.ToVector3d(path.PointAt(slant));
        WorldPicker.ToLatLonAlt(body, far, out var lat, out var lon, out var alt);

        _aLat = lat;
        _aLon = lon;
        _aAlt = alt;
        _hasA = true;
        Dbg.Line($"point A seeded from path far end {_aLat:0.00000} {_aLon:0.00000} {_aAlt:0.0}");
    }

    /// <summary>
    /// A lives outside the draft, so an undo restores the route but not A.
    /// Put A back on the restored path, otherwise the next edit silently moves it.
    /// </summary>
    private void SyncPointAFromDraft()
    {
        if (!_pointMode || !_hasA || _draft == null) return;

        var body = VesselBody();
        if (body == null) return;

        var path = RoutePath.Build(_draft.Draft, body, _settings.DefaultAngle);
        if (path == null) return;

        var slant = Math.Max((double)_draft.Draft.MaxDistance, _draft.Draft.MinDistance);
        var far = RoutePath.ToVector3d(path.PointAt(slant));
        WorldPicker.ToLatLonAlt(body, far, out _aLat, out _aLon, out _aAlt);
        Dbg.Line($"point A synced after undo {_aLat:0.00000} {_aLon:0.00000} {_aAlt:0.0}");
    }

    private void SetPointA()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null) return;
        Change("point A");
        _aLat = vessel.latitude;
        _aLon = RoutePath.NormalizeLon(vessel.longitude);
        _aAlt = vessel.mainBody.GetAltitude(vessel.GetWorldPos3D());
        _hasA = true;
        ApplyTwoPointsLive();
        Dbg.Line($"point A set from vessel {_aLat:0.00000} {_aLon:0.00000} {_aAlt:0.0}");
    }

    private void SetPointB()
    {
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel == null || _draft == null) return;

        var body = vessel.mainBody;
        var bLat = vessel.latitude;
        var bLon = RoutePath.NormalizeLon(vessel.longitude);
        var bAlt = body.GetAltitude(vessel.GetWorldPos3D());

        Change("point B");
        _draft.Draft.Lat = bLat;
        _draft.Draft.Lon = bLon;
        _draft.Draft.AltMsl = bAlt;
        ApplyTwoPointsLive();
        Dbg.Line($"point B set from vessel {bLat:0.00000} {bLon:0.00000} {bAlt:0.0}");
    }

    private bool ApplyTwoPointsLive()
    {
        if (_draft == null || !_pointMode || !_hasA) return false;

        var body = VesselBody();
        if (body == null) return false;

        if (!RouteMath.FromTwoPoints(_draft.Draft, _aLat, _aLon, _aAlt,
                _draft.Draft.Lat, _draft.Draft.Lon, _draft.Draft.AltMsl, body.Radius, out var error))
        {
            return false;
        }

        Dbg.Line($"two points live: hdg={_draft.Draft.HeadingDeg:0.###} ang={_draft.Draft.AngleDeg:0.###} max={_draft.Draft.MaxDistance:0.#}");
        return true;
    }

    private static double VesselHeading(Vessel vessel)
    {
        var body = vessel.mainBody;
        var forward = (Vector3d)vessel.vesselTransform.up; // radial up
        var world = vessel.GetWorldPos3D();
        var ahead = (Vector3d)vessel.vesselTransform.forward;
        var tangent = ahead - forward * Vector3d.Dot(ahead, forward);
        if (tangent.magnitude < 1e-6) return 90.0;

        var sample = world + tangent.normalized * 100.0;
        var lat = body.GetLatitude(sample);
        var lon = body.GetLongitude(sample);
        return RouteMath.BearingDeg(vessel.latitude, vessel.longitude, lat, lon);
    }

    private void RequestCloseEditor()
    {
        if (_draft != null && _draft.IsDirty)
        {
            _confirmDiscard = true;
            return;
        }
        CloseEditor();
    }

    private void Select(string id)
    {
        Dbg.Line($"manual select: '{_settings.ActiveRouteId}' -> '{id}'");
        _settings.AutoRoute = false;
        _settings.ActiveRouteId = id;
        MarkSettingsDirty();
        ClearGates();
    }

    private void SetAuto()
    {
        Dbg.Line("auto mode enabled");
        _settings.AutoRoute = true;
        _nextAutoSelect = 0f;
        var vessel = FlightGlobals.ActiveVessel;
        if (vessel != null) PickAutoRoute(vessel);
        MarkSettingsDirty();
    }

    private void ReadRoutesFromDisk()
    {
        RouteLibrary.Reload(_settings.DefaultAngle);
        RefreshBodyRoutes();
        _path = null;
        _pathFrame = -1;
        _problemsRevision = -1;
        _sharedState = SharedInstallState.Load(SharedStatePath);
        Toast($"{Loc.Get("read_routes")}: {RouteLibrary.Routes.Count}");
        Debug.Log($"[Glissade3D] routes read from disk: {RouteLibrary.Routes.Count}");
    }

    private static string SharedStatePath => Path.Combine(KSPUtil.ApplicationRootPath, "PluginData", "Glissade3D", "shared-installed.cfg");

    private string SharedBaseUrl()
    {
        var configured = string.IsNullOrEmpty(_settings.SharedBaseUrl)
            ? SharedInstaller.DefaultBaseUrl
            : _settings.SharedBaseUrl;
        return SharedInstaller.NormalizeBaseUrl(configured);
    }

    private static string JoinUrl(string baseUrl, string relative)
    {
        if (!baseUrl.EndsWith("/", StringComparison.Ordinal)) baseUrl += "/";
        return baseUrl + relative.TrimStart('/');
    }

    private static IEnumerator FetchBytes(string url, Action<byte[]> done, Action<string> fail)
    {
        using (var request = UnityWebRequest.Get(url))
        {
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError)
            {
                fail(request.error);
                yield break;
            }
            done(request.downloadHandler.data);
        }
    }

    private IEnumerator InstallSharedRoutine()
    {
        _sharedBusy = true;
        _confirmUninstall = false;
        _sharedStatus = Loc.Get("shared_working");

        var baseUrl = SharedBaseUrl();
        var catalog = (SharedCatalog)null;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var error = "";
        Debug.Log($"[Glissade3D] shared install from {baseUrl}");

        if (string.IsNullOrEmpty(baseUrl))
        {
            error = Loc.Get("shared_unconfigured");
        }
        else
        {
            byte[] manifest = null;
            yield return FetchBytes(JoinUrl(baseUrl, SharedInstaller.ManifestName), bytes => manifest = bytes, message => error = message);
            if (manifest != null && string.IsNullOrEmpty(error))
                catalog = SharedCatalog.Parse(Encoding.UTF8.GetString(manifest), out error);

            if (catalog != null)
            {
                var index = 0;
                foreach (var entry in catalog.Entries)
                {
                    index++;
                    _sharedStatus = $"{Loc.Get("shared_working")} {index}/{catalog.Entries.Count}";
                    byte[] bytes = null;
                    yield return FetchBytes(JoinUrl(baseUrl, entry.File), data => bytes = data, message => error = message);
                    if (bytes == null || !string.IsNullOrEmpty(error)) break;
                    files[entry.File] = bytes;
                }
            }
        }

        if (catalog != null && string.IsNullOrEmpty(error) && files.Count == catalog.Entries.Count)
        {
            var result = SharedInstaller.Install(catalog, files, RouteLibrary.Storage, RouteLibrary.Routes, _sharedState, SharedStatePath);
            foreach (var note in result.Notes) Debug.Log("[Glissade3D] shared: " + note);
            _sharedStatus = string.Format(Loc.Get("shared_result"), result.Installed, result.Updated, result.UpToDate + result.Skipped + result.Failed);
            RouteLibrary.Reload(_settings.DefaultAngle);
            RefreshBodyRoutes();
            _path = null;
            _pathFrame = -1;
            Debug.Log($"[Glissade3D] shared install: installed={result.Installed} updated={result.Updated} skipped={result.Skipped} failed={result.Failed}");
        }
        else
        {
            _sharedStatus = string.IsNullOrEmpty(error) ? Loc.Get("shared_failed") : Loc.Get("shared_failed") + ": " + error;
            Debug.LogWarning("[Glissade3D] shared install failed: " + error);
        }

        _sharedState = SharedInstallState.Load(SharedStatePath);
        _sharedBusy = false;
    }

    private void UninstallShared()
    {
        _confirmUninstall = false;
        var result = SharedInstaller.Uninstall(RouteLibrary.Storage, RouteLibrary.Routes, _sharedState, SharedStatePath);
        foreach (var note in result.Notes) Debug.Log("[Glissade3D] shared: " + note);
        _sharedStatus = string.Format(Loc.Get("shared_removed"), result.Removed, result.KeptModified);
        RouteLibrary.Reload(_settings.DefaultAngle);
        RefreshBodyRoutes();
        _path = null;
        _pathFrame = -1;
        _sharedState = SharedInstallState.Load(SharedStatePath);
        Debug.Log($"[Glissade3D] shared uninstall: removed={result.Removed} kept={result.KeptModified} failed={result.Failed}");
    }

    private void CloseMainWindow()
    {
        _settings.ShowWindow = false;
        _rebinding = false;
        _confirmUninstall = false;
        if (_launcher != null) _launcher.SetFalse(false);
        _settingsDirty = false;
        _settings.Save();
    }

    private static bool Matches(Route route, string search)
    {
        if (string.IsNullOrEmpty(search)) return true;
        return route.DisplayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ------------------------------------------------------------------ smoke

    private void RunSmoke()
    {
        _smokeNext = Time.unscaledTime + 0.5f;

        try
        {
            switch (_smokeStep)
            {
                case 0:
                    StorageSmoke();
                    SharedSmoke();
                    Debug.Log($"[Glissade3D][SMOKE] routes={RouteLibrary.Routes.Count} body={_bodyName}");
                    if (RouteLibrary.Routes.Count == 0) Fail("no routes loaded");
                    break;

                case 1:
                    RoundTripCheck();
                    break;

                case 2:
                    CreateRoute();
                    if (_draft == null) { Fail("editor did not open"); break; }
                    Debug.Log($"[Glissade3D][SMOKE] draft created id={_draft.Draft.Id} lat={_draft.Draft.Lat:0.00000} lon={_draft.Draft.Lon:0.00000}");
                    break;

                case 3:
                {
                    if (_draft == null) { Fail("no draft"); break; }
                    Change("angle");
                    _draft.Draft.AngleDeg = 6.5;
                    _draft.BeginChange("taper");
                    _draft.Draft.TaperDistance = 400f;
                    _draft.Draft.EndWidth = 12f;
                    _draft.Draft.EndHeight = 8f;
                    var problems = RouteMath.Validate(_draft.Draft);
                    Debug.Log($"[Glissade3D][SMOKE] validate problems={problems.Count} errors={(RouteMath.HasErrors(problems) ? "yes" : "no")}");
                    if (RouteMath.HasErrors(problems)) Fail("validation errors on a generated route");
                    var normalized = _draft.Draft.Clone();
                    var file = new ConfigNode("Route");
                    normalized.WriteTo(file);
                    var back = Route.ReadFrom(file);
                    Debug.Log($"[Glissade3D][SMOKE] roundtrip equal={back != null && back.SameData(normalized)}");
                    if (back == null || !back.SameData(normalized)) Fail("round-trip mismatch");
                    break;
                }

                case 4:
                    ApplyDraft();
                    if (_editorOpen) Fail("editor still open after apply");
                    else
                    {
                        var appliedPath = RouteLibrary.PathFor(_settings.ActiveRouteId);
                        Debug.Log($"[Glissade3D][SMOKE] route file={appliedPath} exists={File.Exists(appliedPath)}");
                        if (!File.Exists(appliedPath)) Fail("route file not written");
                    }
                    break;

                case 5:
                {
                    // Reload proves the file round-trip, not just the in-memory list.
                    RouteLibrary.Reload(_settings.DefaultAngle);
                    RefreshBodyRoutes();
                    var route = RouteStore.Find(RouteLibrary.Routes, _settings.ActiveRouteId);
                    if (route == null) { Fail("applied route not found after reload"); break; }
                    var reloadedPath = RouteLibrary.PathFor(route.Id);
                    Debug.Log($"[Glissade3D][SMOKE] route file after reload={reloadedPath} exists={File.Exists(reloadedPath)}");
                    if (!File.Exists(reloadedPath)) Fail("route file missing after reload");
                    var path = RoutePath.Build(route, FlightGlobals.ActiveVessel.mainBody, _settings.DefaultAngle);
                    if (path == null) { Fail("path is null"); break; }
                    ClearGates();
                    _builtKey = "";
                    EnsureGates(route);
                    var count = RouteMath.GateCount(route);
                    var ok = _gates.Count == count && count > 1;
                    Debug.Log($"[Glissade3D][SMOKE] gates={_gates.Count} expected={count}");
                    if (!ok) Fail("gate count mismatch");
                    RouteMath.SizeAt(route, 0f, out var w0, out var h0);
                    RouteMath.SizeAt(route, route.TaperDistance, out var w1, out var h1);
                    Debug.Log($"[Glissade3D][SMOKE] pinch={w0:0.0}x{h0:0.0} entry={w1:0.0}x{h1:0.0}");
                    if (Math.Abs(w0 - route.EndWidth) > 1e-3) Fail("taper does not reach pinch size");

                    var steep = route.Clone();
                    steep.AngleFromDefault = false;
                    steep.AngleDeg = -89.0;
                    var steepPath = RoutePath.Build(steep, FlightGlobals.ActiveVessel.mainBody, _settings.DefaultAngle);
                    if (steepPath == null)
                    {
                        Fail("steep path is null");
                        break;
                    }
                    var steepFrame = steepPath.GateAt(100);
                    var finite = !double.IsNaN(steepFrame.Up.X) && !double.IsNaN(steepFrame.Up.Y) && !double.IsNaN(steepFrame.Up.Z);
                    Debug.Log($"[Glissade3D][SMOKE] steep angle=-89 finite={finite} up=({steepFrame.Up.X:0.###},{steepFrame.Up.Y:0.###},{steepFrame.Up.Z:0.###})");
                    if (!finite) Fail("steep angle frame is not finite");
                    break;
                }

                case 6:
                    TogglePause();
                    Debug.Log($"[Glissade3D][SMOKE] timeScale after pause={Time.timeScale}");
                    break;

                case 7:
                    ReleasePause();
                    Debug.Log($"[Glissade3D][SMOKE] timeScale after release={Time.timeScale}");
                    if (Time.timeScale <= 0f) Fail("timeScale was not restored");
                    break;

                case 8:
                {
                    var vessel = FlightGlobals.ActiveVessel;
                    var route = RouteStore.Find(RouteLibrary.Routes, _settings.ActiveRouteId);
                    if (route == null || vessel == null) { Fail("marker step has no route"); break; }
                    var body = vessel.mainBody;
                    var point = body.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
                    var ground = WorldPicker.GroundPoint(body, route.Lat, route.Lon);
                    var up = (point - body.position).normalized;
                    _marker.SetVisible(true);
                    var smokeCamera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
                    var smokeFov = smokeCamera != null ? smokeCamera.fieldOfView : 60f;
                    _marker.Update(vessel.transform.position, vessel.GetWorldPos3D(), point, up, ground,
                        (point - vessel.GetWorldPos3D()).magnitude, smokeFov, Screen.height);
                    Debug.Log("[Glissade3D][SMOKE] marker updated");
                    break;
                }

                case 9:
                {
                    var camera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
                    if (camera == null)
                    {
                        Debug.Log("[Glissade3D][SMOKE] camera absent, pick skipped");
                        break;
                    }
                    var route = RouteStore.Find(RouteLibrary.Routes, _settings.ActiveRouteId);
                    var current = FlightGlobals.ActiveVessel.mainBody.GetWorldSurfacePosition(route.Lat, route.Lon, route.AltMsl);
                    var picked = WorldPicker.TryPick(camera, current, out var world);
                    Debug.Log($"[Glissade3D][SMOKE] pick={picked} finite={!double.IsNaN(world.x)}");
                    break;
                }

                case 10:
                {
                    var id = _settings.ActiveRouteId;
                    var deletedPath = RouteLibrary.PathFor(id);
                    if (!RouteLibrary.DeleteUser(id, out var error)) Fail("delete failed: " + error);
                    var gone = RouteStore.Find(RouteLibrary.Routes, id) == null;
                    Debug.Log($"[Glissade3D][SMOKE] deleted={gone} fileGone={!File.Exists(deletedPath)}");
                    if (!gone) Fail("route still present after delete");
                    if (!string.IsNullOrEmpty(deletedPath) && File.Exists(deletedPath)) Fail("route file still present after delete");
                    break;
                }

                default:
                    Debug.Log($"[Glissade3D][SMOKE] DONE failures={_smokeFailures}");
                    _smokeStep = -1;
                    return;
            }
        }
        catch (Exception e)
        {
            Fail(e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }

        _smokeStep++;
    }

    private void SharedSmoke()
    {
        var dir = Path.Combine(Path.GetTempPath(), "glissade-shared-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var route = new Route
            {
                Id = "5h4r3d000001",
                Name = "Shared smoke",
                Body = "Kerbin",
                Lat = -0.05,
                Lon = 285.3,
                AltMsl = 70,
                HeadingDeg = 90,
                AngleDeg = 3,
                MinDistance = 250f,
                MaxDistance = 5000f,
                Spacing = 500f,
                Width = 40f,
                Height = 20f,
                EndWidth = 20f,
                EndHeight = 10f,
                TaperDistance = 400f
            };

            var file = Path.Combine(dir, "route.cfg");
            var wrapper = new ConfigNode("GlissadeRoutes");
            var node = new ConfigNode("Route");
            route.WriteTo(node);
            wrapper.AddNode(node);
            if (!ConfigIo.Save(wrapper, file, true, out var writeError))
            {
                Fail("shared smoke write: " + writeError);
                return;
            }

            var bytes = File.ReadAllBytes(file);
            var manifest = "SharedRoutes\n{\n    version = 1\n    Entry\n    {\n" +
                           $"        id = {route.Id}\n        name = {route.Name}\n        file = route.cfg\n" +
                           $"        sha256 = {SharedHash.Bytes(bytes)}\n" +
                           "    }\n}\n";
            var catalog = SharedCatalog.Parse(manifest, out var parseError);
            if (catalog == null)
            {
                Fail("shared smoke catalog: " + parseError);
                return;
            }

            var storage = new RouteStorage(dir);
            var routes = new List<Route>();
            var statePath = Path.Combine(dir, "shared-installed.cfg");
            var files = new Dictionary<string, byte[]> { { "route.cfg", bytes } };
            var install = SharedInstaller.Install(catalog, files, storage, routes, new SharedInstallState(), statePath);
            if (install.Installed != 1 || install.Failed != 0)
            {
                Fail("shared smoke install");
                return;
            }

            var loaded = storage.Load(_settings.DefaultAngle);
            if (loaded.Routes.Count != 1)
            {
                Fail("shared smoke load");
                return;
            }

            var uninstall = SharedInstaller.Uninstall(storage, loaded.Routes, SharedInstallState.Load(statePath), statePath);
            if (uninstall.Removed != 1)
            {
                Fail("shared smoke uninstall");
                return;
            }

            Debug.Log("[Glissade3D][SMOKE] shared ok");
        }
        catch (Exception e)
        {
            Fail("shared smoke: " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private void StorageSmoke()
    {
        var dir = Path.Combine(Path.GetTempPath(), "glissade-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var storage = new RouteStorage(dir);
            var route = new Route
            {
                Id = "5m0ke0000001",
                Name = "Smoke route",
                Body = "Kerbin",
                Lat = -0.05,
                Lon = 285.3,
                AltMsl = 70,
                HeadingDeg = 90,
                AngleDeg = 3,
                MinDistance = 250f,
                MaxDistance = 10000f,
                Spacing = 500f,
                Width = 40f,
                Height = 20f,
                EndWidth = 40f,
                EndHeight = 20f,
                TaperDistance = 0f
            };

            if (!storage.Save(route, out var saveError)) { Fail("storage save: " + saveError); return; }
            var path = storage.PathFor(route.Id);
            if (!File.Exists(path)) { Fail("storage file missing"); return; }

            var loaded = new RouteStorage(dir).Load(_settings.DefaultAngle);
            if (loaded.Routes.Count != 1) { Fail("storage load count=" + loaded.Routes.Count); return; }
            if (!loaded.Routes[0].SameData(route)) { Fail("storage roundtrip data"); return; }

            var legacyDir = Path.Combine(dir, "legacy");
            Directory.CreateDirectory(legacyDir);
            var root = new ConfigNode("GlissadeRoutes");
            var node = new ConfigNode("Route");
            route.WriteTo(node);
            root.AddNode(node);
            if (!ConfigIo.Save(root, Path.Combine(legacyDir, "routes.cfg"), true, out var legacyError))
            {
                Fail("storage legacy write: " + legacyError);
                return;
            }

            var migrated = new RouteStorage(legacyDir).Load(_settings.DefaultAngle);
            if (migrated.Routes.Count != 1) { Fail("storage migration count=" + migrated.Routes.Count); return; }
            if (Directory.GetFiles(legacyDir, "routes.cfg.migrated-*").Length != 1)
            {
                Fail("storage migration archive");
                return;
            }

            Debug.Log("[Glissade3D][SMOKE] storage ok");
        }
        catch (Exception e)
        {
            Fail("storage: " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    private void RoundTripCheck()
    {
        var vessel = FlightGlobals.ActiveVessel;
        var body = vessel.mainBody;
        var world = vessel.GetWorldPos3D();
        WorldPicker.ToLatLonAlt(body, world, out var lat, out var lon, out var alt);
        var back = body.GetWorldSurfacePosition(lat, lon, alt);
        var error = (back - world).magnitude;
        Debug.Log($"[Glissade3D][SMOKE] coord roundtrip lat={lat:0.00000} lon={lon:0.00000} alt={alt:0.0} error={error:0.00} m");
        if (error > 5.0) Fail($"coordinate round-trip error {error:0.0} m");
    }

    private void Fail(string message)
    {
        _smokeFailures++;
        Debug.LogError($"[Glissade3D][SMOKE] FAIL step {_smokeStep}: {message}");
    }

    private static Texture2D BuildIcon()
    {
        const int size = 38;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var outer = x >= 6 && x <= 31 && y >= 10 && y <= 27;
                var inner = x >= 10 && x <= 27 && y >= 14 && y <= 23;
                pixels[y * size + x] = outer && !inner
                    ? new Color32(40, 220, 90, 255)
                    : new Color32(0, 0, 0, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Point;
        return texture;
    }
}
