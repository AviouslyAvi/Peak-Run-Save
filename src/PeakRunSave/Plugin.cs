using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Photon.Pun;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zorro.Core.Serizalization;

namespace PeakRunSave;

[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    [Serializable]
    private sealed class SavedSlot
    {
        public bool hasItem;
        public ushort itemId;
    }

    [Serializable]
    private sealed class PlayerSnapshot
    {
        public int actorNumber;
        public string characterName = string.Empty;
        public Vector3 position;
        public Quaternion rotation;
        public bool hasHealth;
        public float passOutValue;
        public float stamina;
        public List<SavedSlot> slots = new();
        public SavedSlot[] slotsArray = Array.Empty<SavedSlot>();
    }

    [Serializable]
    private sealed class RunSnapshot
    {
        public int version = 2;
        public bool hasSeed;
        public int seed;
        public bool hasSceneName;
        public string sceneName = string.Empty;
        public bool hasSegment;
        public int segment;
        public bool hasLocalTransform;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public List<PlayerSnapshot> players = new();
        public PlayerSnapshot[] playersArray = Array.Empty<PlayerSnapshot>();

        // Backward compatibility with v1 snapshots.
        public bool hasHealth;
        public float passOutValue;
        public float stamina;
        public List<SavedSlot> slots = new();
        public SavedSlot[] slotsArray = Array.Empty<SavedSlot>();
    }

    private sealed class PendingLocalRestore
    {
        public bool hasTransform;
        public Vector3 position;
        public Quaternion rotation;
        public bool hasHealth;
        public float passOutValue;
        public float stamina;
        public List<SavedSlot> slots = new();
    }

    internal static ManualLogSource Log { get; private set; } = null!;

    private ConfigEntry<KeyCode> _saveKey = null!;
    private ConfigEntry<KeyCode> _loadKey = null!;
    private ConfigEntry<KeyCode> _menuKey = null!;
    private ConfigEntry<int> _menuSlots = null!;
    private ConfigEntry<bool> _hostOnlyControls = null!;
    private ConfigEntry<bool> _restoreHealth = null!;
    private ConfigEntry<bool> _restoreAllPlayerPositions = null!;
    private ConfigEntry<bool> _restoreAllPlayerInventories = null!;
    private ConfigEntry<bool> _experimentalRemoteInventoryRestore = null!;
    private ConfigEntry<bool> _preferSceneLoadOnRestore = null!;
    private ConfigEntry<string> _saveFileName = null!;
    private ConfigEntry<float> _groundRaycastHeight = null!;
    private ConfigEntry<float> _groundRaycastDistance = null!;

    private int? _pendingSeed;
    private float _nextSeedApplyTime;
    private int? _pendingSegment;
    private string? _pendingSceneName;
    private bool _pendingSegmentStartRequested;
    private List<PlayerSnapshot>? _pendingPlayersRestore;
    private float _pendingPlayersRestoreAt;
    private PendingLocalRestore? _pendingLocalRestore;
    private float _pendingLocalRestoreAt;
    private bool _showMenu;
    private Rect _menuRect = new(30f, 30f, 470f, 360f);
    private string _menuStatus = string.Empty;
    private float _menuStatusUntil;

    private string SaveDirectory
    {
        get
        {
            if (Path.IsPathRooted(_saveFileName.Value))
            {
                var rootedDirectory = Path.GetDirectoryName(_saveFileName.Value);
                if (!string.IsNullOrWhiteSpace(rootedDirectory))
                {
                    Directory.CreateDirectory(rootedDirectory);
                    return rootedDirectory;
                }

                return Paths.ConfigPath;
            }

            var folder = Path.Combine(Paths.ConfigPath, "PeakRunSave");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private string SaveFilePath => Path.IsPathRooted(_saveFileName.Value)
        ? _saveFileName.Value
        : Path.Combine(SaveDirectory, _saveFileName.Value);

    private void Awake()
    {
        Log = Logger;

        _saveKey = Config.Bind("General", "SaveKey", KeyCode.F5, "Hotkey that snapshots your current run.");
        _loadKey = Config.Bind("General", "LoadKey", KeyCode.F9, "Hotkey that loads the last snapshot.");
        _menuKey = Config.Bind("General", "MenuKey", KeyCode.F10, "Hotkey that opens/closes the save menu.");
        _menuSlots = Config.Bind("General", "MenuSlots", 5, "How many save slots to show in the save menu.");
        _hostOnlyControls = Config.Bind("General", "HostOnlyControls", true, "If true, only the lobby host/master can save/load snapshots.");
        _restoreHealth = Config.Bind("General", "RestoreHealth", true, "If true, restore pass-out value and stamina on load.");
        _restoreAllPlayerPositions = Config.Bind("Multiplayer", "RestoreAllPlayerPositions", true, "If true, restore saved positions for every matched player actor.");
        _restoreAllPlayerInventories = Config.Bind("Multiplayer", "RestoreAllPlayerInventories", true, "If true, restore inventory where safe.");
        _experimentalRemoteInventoryRestore = Config.Bind("Multiplayer", "ExperimentalRemoteInventoryRestore", false, "If true, attempt remote inventory RPC restore for non-local players. Can be unstable.");
        _preferSceneLoadOnRestore = Config.Bind("General", "PreferSceneLoadOnRestore", true, "If true, load the saved scene first, then restore segment/players.");
        _groundRaycastHeight = Config.Bind("Multiplayer", "GroundRaycastHeight", 6f, "Height above target position used for ground snap raycast.");
        _groundRaycastDistance = Config.Bind("Multiplayer", "GroundRaycastDistance", 64f, "Distance used for ground snap raycast.");
        _saveFileName = Config.Bind("General", "SaveFile", "run_snapshot.json", "Filename under BepInEx/config/PeakRunSave.");

        SceneManager.sceneLoaded += OnSceneLoaded;

        Log.LogInfo($"{Name} loaded. Save: {_saveKey.Value}, Load: {_loadKey.Value}");
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_saveKey.Value))
        {
            if (!CanUseControl("save"))
            {
                return;
            }

            SaveSnapshot();
        }

        if (Input.GetKeyDown(_loadKey.Value))
        {
            if (!CanUseControl("load"))
            {
                return;
            }

            LoadSnapshot();
        }

        if (Input.GetKeyDown(_menuKey.Value))
        {
            _showMenu = !_showMenu;
        }

        if (_pendingSeed.HasValue && Time.unscaledTime >= _nextSeedApplyTime)
        {
            TryApplyPendingSeed();
        }

        if (_pendingSegment.HasValue)
        {
            TryApplyPendingSegment();
        }

        if (!string.IsNullOrWhiteSpace(_pendingSceneName))
        {
            TryApplyPendingScene();
        }

        if (_pendingPlayersRestore != null)
        {
            TryApplyPendingPlayersRestore();
        }

        if (_pendingLocalRestore != null)
        {
            TryApplyPendingLocalRestore();
        }

        if (_menuStatusUntil > 0f && Time.unscaledTime > _menuStatusUntil)
        {
            _menuStatus = string.Empty;
            _menuStatusUntil = 0f;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _nextSeedApplyTime = Time.unscaledTime;

        if (!string.IsNullOrWhiteSpace(_pendingSceneName) && string.Equals(scene.name, _pendingSceneName, StringComparison.OrdinalIgnoreCase))
        {
            Log.LogInfo($"Reached pending scene '{_pendingSceneName}'.");
            _pendingSceneName = null;
        }
    }

    private bool CanUseControl(string action)
    {
        if (!_hostOnlyControls.Value)
        {
            return true;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            return true;
        }

        Log.LogWarning($"Ignoring {action}: host-only mode is enabled and this client is not host/master.");
        return false;
    }

    private void SaveSnapshot()
    {
        SaveSnapshotToPath(SaveFilePath);
    }

    private void LoadSnapshot()
    {
        LoadSnapshotFromPath(SaveFilePath);
    }

    private bool SaveSnapshotToPath(string path)
    {
        var localPlayer = Player.localPlayer;
        if (localPlayer == null)
        {
            Log.LogWarning("Cannot save: local player is not ready.");
            SetMenuStatus("Cannot save: local player is not ready.");
            return false;
        }

        var snapshot = new RunSnapshot();
        var seed = DetectCurrentSeed();
        if (seed.HasValue)
        {
            snapshot.hasSeed = true;
            snapshot.seed = seed.Value;
        }

        var activeScene = SceneManager.GetActiveScene();
        if (!string.IsNullOrWhiteSpace(activeScene.name))
        {
            snapshot.hasSceneName = true;
            snapshot.sceneName = activeScene.name;
        }

        if (MapHandler.Exists)
        {
            snapshot.hasSegment = true;
            snapshot.segment = (int)MapHandler.CurrentSegmentNumber;
        }

        var allCharacters = PlayerHandler.GetAllPlayerCharacters();
        foreach (var character in allCharacters)
        {
            var playerSnapshot = CreatePlayerSnapshot(character);
            if (playerSnapshot != null)
            {
                snapshot.players.Add(playerSnapshot);
            }
        }

        if (snapshot.players.Count == 0 && localPlayer.character != null)
        {
            var fallback = CreatePlayerSnapshot(localPlayer.character);
            if (fallback != null)
            {
                snapshot.players.Add(fallback);
            }
        }

        // Maintain compatibility with older single-player snapshot fields.
        CopyLegacyLocalFields(localPlayer, snapshot);
        snapshot.playersArray = snapshot.players.ToArray();

        var localCharacter = localPlayer.character;
        if (localCharacter != null)
        {
            snapshot.hasLocalTransform = true;
            snapshot.localPosition = localCharacter.transform.position;
            snapshot.localRotation = localCharacter.transform.rotation;
        }

        var json = JsonUtility.ToJson(snapshot, true);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);

        Log.LogInfo($"Saved snapshot to '{path}'. Seed: {(snapshot.hasSeed ? snapshot.seed : -1)}, scene: {snapshot.sceneName}, segment: {(snapshot.hasSegment ? snapshot.segment : -1)}, players: {snapshot.players.Count}");
        SetMenuStatus($"Saved: {Path.GetFileName(path)}");
        return true;
    }

    private static void CopyLegacyLocalFields(Player localPlayer, RunSnapshot snapshot)
    {
        var data = localPlayer.character?.data;
        if (data != null)
        {
            snapshot.hasHealth = true;
            snapshot.passOutValue = data.passOutValue;
            snapshot.stamina = data.currentStamina;
        }

        snapshot.slots.Clear();
        snapshot.slots.AddRange(SerializeInventory(localPlayer));
        snapshot.slotsArray = snapshot.slots.ToArray();
    }

    private static PlayerSnapshot? CreatePlayerSnapshot(Character character)
    {
        if (character == null)
        {
            return null;
        }

        var photonView = character.photonView;
        if (photonView == null)
        {
            return null;
        }

        var playerSnapshot = new PlayerSnapshot
        {
            actorNumber = photonView.OwnerActorNr,
            characterName = character.characterName ?? string.Empty,
            position = character.transform.position,
            rotation = character.transform.rotation
        };

        var data = character.data;
        if (data != null)
        {
            playerSnapshot.hasHealth = true;
            playerSnapshot.passOutValue = data.passOutValue;
            playerSnapshot.stamina = data.currentStamina;
        }

        var player = character.player;
        if (player != null)
        {
            playerSnapshot.slots.AddRange(SerializeInventory(player));
            playerSnapshot.slotsArray = playerSnapshot.slots.ToArray();
        }

        return playerSnapshot;
    }

    private bool LoadSnapshotFromPath(string path)
    {
        if (!File.Exists(path))
        {
            Log.LogWarning($"No snapshot found at '{path}'.");
            SetMenuStatus($"No save file: {Path.GetFileName(path)}");
            return false;
        }

        var json = File.ReadAllText(path);
        var snapshot = JsonUtility.FromJson<RunSnapshot>(json);
        if (snapshot == null)
        {
            Log.LogError("Snapshot parse failed.");
            SetMenuStatus("Snapshot parse failed.");
            return false;
        }

        var localPlayer = Player.localPlayer;
        if (localPlayer == null)
        {
            Log.LogWarning("Cannot load: local player is not ready.");
            SetMenuStatus("Cannot load: local player is not ready.");
            return false;
        }

        if (snapshot.hasSeed)
        {
            _pendingSeed = snapshot.seed;
            _nextSeedApplyTime = Time.unscaledTime;
            TryApplyPendingSeed();
        }

        var deferredRestore = false;
        var sceneScheduled = false;
        if (_preferSceneLoadOnRestore.Value && snapshot.hasSceneName)
        {
            sceneScheduled = TryScheduleSceneLoad(snapshot.sceneName);
            deferredRestore = deferredRestore || sceneScheduled;
        }

        if (snapshot.hasSegment)
        {
            if (!sceneScheduled)
            {
                deferredRestore = deferredRestore || TryScheduleSegmentLoad(snapshot.segment);
            }
            else
            {
                _pendingSegment = snapshot.segment;
                _pendingSegmentStartRequested = false;
                Log.LogInfo($"Queued segment restore ({snapshot.segment}) until scene load completes.");
            }
        }
        else if (!sceneScheduled && snapshot.hasSceneName)
        {
            deferredRestore = deferredRestore || TryScheduleSceneLoad(snapshot.sceneName);
        }

        var snapshotPlayers = GetSnapshotPlayers(snapshot);
        if (snapshotPlayers.Count > 0)
        {
            if (deferredRestore)
            {
                _pendingPlayersRestore = snapshotPlayers;
                _pendingPlayersRestoreAt = Time.unscaledTime + 2f;
                Log.LogInfo($"Deferred player restore until segment load finishes. Target segment: {snapshot.segment}");
            }
            else
            {
                RestorePlayersSnapshot(snapshotPlayers);
            }
        }
        else
        {
            // Legacy v1 snapshot fallback.
            RestoreInventory(localPlayer, GetSnapshotSlots(snapshot), false);
            if (_restoreHealth.Value)
            {
                RestoreHealth(localPlayer.character, snapshot.passOutValue, snapshot.stamina, snapshot.hasHealth);
            }
        }

        var localApplied = ApplyLocalFallbackNow(snapshot);
        if (!localApplied)
        {
            _pendingLocalRestore = new PendingLocalRestore
            {
                hasTransform = snapshot.hasLocalTransform,
                position = snapshot.localPosition,
                rotation = snapshot.localRotation,
                hasHealth = snapshot.hasHealth,
                passOutValue = snapshot.passOutValue,
                stamina = snapshot.stamina,
                slots = GetSnapshotSlots(snapshot)
            };
            _pendingLocalRestoreAt = Time.unscaledTime + (deferredRestore ? 2.25f : 0.5f);
            Log.LogInfo("Queued pending local fallback restore.");
        }

        Log.LogInfo("Snapshot loaded.");
        SetMenuStatus($"Loaded: {Path.GetFileName(path)}");
        return true;
    }

    private bool TryScheduleSceneLoad(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return false;
        }

        var currentScene = SceneManager.GetActiveScene().name;
        if (string.Equals(currentScene, sceneName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient)
            {
                PhotonNetwork.LoadLevel(sceneName);
                _pendingSceneName = sceneName;
                SetMenuStatus($"Loading scene: {sceneName}");
                Log.LogInfo($"Scheduled network scene load to '{sceneName}'.");
                return true;
            }

            SceneManager.LoadScene(sceneName);
            _pendingSceneName = sceneName;
            SetMenuStatus($"Loading scene: {sceneName}");
            Log.LogInfo($"Scheduled local scene load to '{sceneName}'.");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to schedule scene load '{sceneName}': {ex.Message}");
            return false;
        }
    }

    private bool TryScheduleSegmentLoad(int segmentValue)
    {
        if (!TryGetSegment(segmentValue, out var target))
        {
            Log.LogWarning($"Saved segment value '{segmentValue}' is invalid.");
            return false;
        }
        if (MapHandler.Exists)
        {
            var current = MapHandler.CurrentSegmentNumber;
            if (current == target)
            {
                return false;
            }

            Log.LogInfo($"Scheduling segment jump from {current} to {target}.");
            MapHandler.SetSegmentOnSpawn(target, 0);
            MapHandler.JumpToSegment(target);
            _pendingSegment = segmentValue;
            _pendingSegmentStartRequested = false;
            SetMenuStatus($"Loading segment: {target}");
            return true;
        }

        _pendingSegment = segmentValue;
        _pendingSegmentStartRequested = false;
        SetMenuStatus($"Waiting to load segment: {target}");
        return true;
    }

    private string GetMenuSlotPath(int slotIndex)
    {
        var slot = Mathf.Max(1, slotIndex + 1);
        return Path.Combine(SaveDirectory, $"run_snapshot_slot_{slot}.json");
    }

    private static string TimestampLabel(string path)
    {
        if (!File.Exists(path))
        {
            return "empty";
        }

        return File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void SetMenuStatus(string text)
    {
        _menuStatus = text;
        _menuStatusUntil = Time.unscaledTime + 6f;
    }

    private void OnGUI()
    {
        if (!_showMenu)
        {
            return;
        }

        _menuRect = GUI.Window(294031, _menuRect, DrawMenuWindow, "Peak Run Save");
    }

    private void DrawMenuWindow(int windowId)
    {
        GUI.Label(new Rect(15f, 25f, 440f, 20f), $"Host: {(PhotonNetwork.IsMasterClient ? "Yes" : "No")} | Menu key: {_menuKey.Value}");
        var hostOnlyText = _hostOnlyControls.Value ? "Host-only controls: ON" : "Host-only controls: OFF";
        GUI.Label(new Rect(15f, 45f, 440f, 20f), hostOnlyText);

        if (GUI.Button(new Rect(15f, 72f, 150f, 28f), "Save Default"))
        {
            if (CanUseControl("save"))
            {
                SaveSnapshotToPath(SaveFilePath);
            }
        }

        if (GUI.Button(new Rect(175f, 72f, 150f, 28f), "Load Default"))
        {
            if (CanUseControl("load"))
            {
                LoadSnapshotFromPath(SaveFilePath);
            }
        }

        GUI.Label(new Rect(335f, 77f, 120f, 20f), Path.GetFileName(SaveFilePath));

        var slots = Mathf.Clamp(_menuSlots.Value, 1, 12);
        var y = 110f;
        for (var i = 0; i < slots; i++)
        {
            var path = GetMenuSlotPath(i);
            var slotName = $"Slot {i + 1}";
            if (GUI.Button(new Rect(15f, y, 70f, 26f), $"Save {i + 1}"))
            {
                if (CanUseControl("save"))
                {
                    SaveSnapshotToPath(path);
                }
            }

            if (GUI.Button(new Rect(90f, y, 70f, 26f), $"Load {i + 1}"))
            {
                if (CanUseControl("load"))
                {
                    LoadSnapshotFromPath(path);
                }
            }

            GUI.Label(new Rect(170f, y + 5f, 280f, 20f), $"{slotName}: {TimestampLabel(path)}");
            y += 30f;
        }

        if (!string.IsNullOrWhiteSpace(_menuStatus))
        {
            GUI.Label(new Rect(15f, _menuRect.height - 45f, 440f, 22f), _menuStatus);
        }

        if (GUI.Button(new Rect(_menuRect.width - 90f, _menuRect.height - 34f, 75f, 24f), "Close"))
        {
            _showMenu = false;
        }

        GUI.DragWindow(new Rect(0f, 0f, _menuRect.width, 22f));
    }

    private void RestorePlayersSnapshot(List<PlayerSnapshot> players)
    {
        var currentCharacters = PlayerHandler.GetAllPlayerCharacters();
        var byActor = new Dictionary<int, Character>(currentCharacters.Count);
        foreach (var character in currentCharacters)
        {
            if (character?.photonView == null)
            {
                continue;
            }

            var actorNumber = character.photonView.OwnerActorNr;
            if (!byActor.ContainsKey(actorNumber))
            {
                byActor.Add(actorNumber, character);
            }
        }

        var missingActors = new List<int>();
        foreach (var playerSnapshot in players)
        {
            if (!byActor.TryGetValue(playerSnapshot.actorNumber, out var character))
            {
                missingActors.Add(playerSnapshot.actorNumber);
                continue;
            }

            try
            {
                if (_restoreAllPlayerPositions.Value)
                {
                    RestorePlayerPosition(character, playerSnapshot);
                }

                if (_restoreAllPlayerInventories.Value)
                {
                    var player = character.player;
                    if (player != null)
                    {
                        RestoreInventory(player, playerSnapshot.slots, _experimentalRemoteInventoryRestore.Value);
                    }
                }

                if (_restoreHealth.Value)
                {
                    RestoreHealth(character, playerSnapshot.passOutValue, playerSnapshot.stamina, playerSnapshot.hasHealth);
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed restoring actor {playerSnapshot.actorNumber}: {ex}");
            }
        }

        if (missingActors.Count > 0)
        {
            Log.LogWarning($"Could not match {missingActors.Count} saved players in current lobby: {string.Join(", ", missingActors)}");
        }
    }

    private void RestorePlayerPosition(Character character, PlayerSnapshot playerSnapshot)
    {
        var safePosition = GetSafeGroundedPosition(playerSnapshot.position);
        var photonView = character.photonView;
        if (photonView != null)
        {
            photonView.RPC("WarpPlayerRPC", RpcTarget.All, safePosition, false);
        }
        else
        {
            Log.LogWarning($"Character photonView missing for actor {playerSnapshot.actorNumber}, applying transform fallback.");
        }

        // Immediate local correction on host so position update is not visually delayed.
        character.transform.position = safePosition;
        character.transform.rotation = playerSnapshot.rotation;
        character.LastLivingPosition = safePosition;
    }

    private Vector3 GetSafeGroundedPosition(Vector3 targetPosition)
    {
        var origin = targetPosition + Vector3.up * Mathf.Max(0.5f, _groundRaycastHeight.Value);
        if (Physics.Raycast(origin, Vector3.down, out var hit, Mathf.Max(2f, _groundRaycastDistance.Value), Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * 0.35f;
        }

        return targetPosition;
    }

    private static List<SavedSlot> SerializeInventory(Player player)
    {
        var slots = new List<SavedSlot>();
        if (player.itemSlots == null)
        {
            return slots;
        }

        foreach (var slot in player.itemSlots)
        {
            var prefab = slot?.prefab;
            slots.Add(new SavedSlot
            {
                hasItem = prefab != null,
                itemId = prefab != null ? prefab.itemID : (ushort)0
            });
        }

        return slots;
    }

    private static int? DetectCurrentSeed()
    {
        var levelGeneration = FindFirstObjectByType<LevelGeneration>();
        if (levelGeneration != null)
        {
            return levelGeneration.seed;
        }

        var mapGenerator = FindFirstObjectByType<MapGenerator>();
        if (mapGenerator != null)
        {
            return mapGenerator.seed;
        }

        return null;
    }

    private void TryApplyPendingSeed()
    {
        if (!_pendingSeed.HasValue)
        {
            return;
        }

        var seed = _pendingSeed.Value;

        var setAny = false;
        var sawLevel = false;
        var sawMap = false;

        var levelGeneration = FindFirstObjectByType<LevelGeneration>();
        if (levelGeneration != null)
        {
            sawLevel = true;
            levelGeneration.seed = seed;
            setAny = true;
        }

        var mapGenerator = FindFirstObjectByType<MapGenerator>();
        if (mapGenerator != null)
        {
            sawMap = true;
            mapGenerator.seed = seed;
            setAny = true;
        }

        if (setAny)
        {
            Log.LogInfo($"Applied pending seed {seed}.");
            _pendingSeed = null;
            return;
        }

        if (sawLevel && sawMap)
        {
            _pendingSeed = null;
            return;
        }

        _nextSeedApplyTime = Time.unscaledTime + 0.5f;
    }

    private void TryApplyPendingSegment()
    {
        if (!_pendingSegment.HasValue)
        {
            return;
        }

        var segmentValue = _pendingSegment.Value;
        if (!TryGetSegment(segmentValue, out var target))
        {
            Log.LogWarning($"Pending segment '{segmentValue}' is invalid. Clearing.");
            _pendingSegment = null;
            _pendingSegmentStartRequested = false;
            return;
        }
        if (!MapHandler.Exists)
        {
            if (!string.IsNullOrWhiteSpace(_pendingSceneName))
            {
                return;
            }

            if (_pendingSegmentStartRequested)
            {
                return;
            }

            var starter = FindFirstObjectByType<RunStarter>();
            if (starter != null)
            {
                Log.LogInfo($"RunStarter detected. Starting run for pending segment {target}.");
                starter.StartRun();
                _pendingSegmentStartRequested = true;
                return;
            }

            var manager = RunManager.Instance ?? FindFirstObjectByType<RunManager>();
            if (manager != null)
            {
                Log.LogInfo($"RunManager detected. Starting run for pending segment {target}.");
                manager.StartRun();
                _pendingSegmentStartRequested = true;
            }

            return;
        }

        var current = MapHandler.CurrentSegmentNumber;
        if (current != target)
        {
            Log.LogInfo($"Applying pending segment jump from {current} to {target}.");
            MapHandler.SetSegmentOnSpawn(target, 0);
            MapHandler.JumpToSegment(target);
        }
        else
        {
            Log.LogInfo($"Pending segment {target} is already active.");
        }

        _pendingSegment = null;
        _pendingSegmentStartRequested = false;
    }

    private static bool TryGetSegment(int value, out Segment segment)
    {
        segment = default;
        if (value < byte.MinValue || value > byte.MaxValue)
        {
            return false;
        }

        var raw = (byte)value;
        if (!Enum.IsDefined(typeof(Segment), raw))
        {
            return false;
        }

        segment = (Segment)raw;
        return true;
    }

    private void TryApplyPendingPlayersRestore()
    {
        if (_pendingPlayersRestore == null)
        {
            return;
        }

        if (_pendingSegment.HasValue)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingSceneName))
        {
            return;
        }

        if (Time.unscaledTime < _pendingPlayersRestoreAt)
        {
            return;
        }

        var players = _pendingPlayersRestore;
        _pendingPlayersRestore = null;
        RestorePlayersSnapshot(players);
        SetMenuStatus("Applied deferred player restore.");
    }

    private bool ApplyLocalFallbackNow(RunSnapshot snapshot)
    {
        var localPlayer = Player.localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        if (snapshot.hasLocalTransform && localPlayer.character != null)
        {
            var safe = GetSafeGroundedPosition(snapshot.localPosition);
            var character = localPlayer.character;
            var view = character.photonView;
            if (view != null)
            {
                view.RPC("WarpPlayerRPC", RpcTarget.All, safe, false);
            }

            character.transform.position = safe;
            character.transform.rotation = snapshot.localRotation;
            character.LastLivingPosition = safe;
            Log.LogInfo($"Applied local fallback transform restore to {safe}.");
        }

        if (_restoreAllPlayerInventories.Value)
        {
            RestoreInventory(localPlayer, GetSnapshotSlots(snapshot), false);
        }

        if (_restoreHealth.Value)
        {
            RestoreHealth(localPlayer.character, snapshot.passOutValue, snapshot.stamina, snapshot.hasHealth);
        }

        return true;
    }

    private void TryApplyPendingLocalRestore()
    {
        if (_pendingLocalRestore == null)
        {
            return;
        }

        if (_pendingSegment.HasValue || !string.IsNullOrWhiteSpace(_pendingSceneName))
        {
            return;
        }

        if (Time.unscaledTime < _pendingLocalRestoreAt)
        {
            return;
        }

        var localPlayer = Player.localPlayer;
        if (localPlayer == null)
        {
            _pendingLocalRestoreAt = Time.unscaledTime + 0.5f;
            return;
        }

        var pending = _pendingLocalRestore;
        if (pending.hasTransform && localPlayer.character != null)
        {
            var safe = GetSafeGroundedPosition(pending.position);
            var character = localPlayer.character;
            var view = character.photonView;
            if (view != null)
            {
                view.RPC("WarpPlayerRPC", RpcTarget.All, safe, false);
            }

            character.transform.position = safe;
            character.transform.rotation = pending.rotation;
            character.LastLivingPosition = safe;
            Log.LogInfo($"Applied pending local transform restore to {safe}.");
        }

        if (_restoreAllPlayerInventories.Value)
        {
            RestoreInventory(localPlayer, pending.slots, false);
        }

        if (_restoreHealth.Value)
        {
            RestoreHealth(localPlayer.character, pending.passOutValue, pending.stamina, pending.hasHealth);
        }

        _pendingLocalRestore = null;
        SetMenuStatus("Applied local fallback restore.");
    }

    private void TryApplyPendingScene()
    {
        if (string.IsNullOrWhiteSpace(_pendingSceneName))
        {
            return;
        }

        var currentScene = SceneManager.GetActiveScene().name;
        if (string.Equals(currentScene, _pendingSceneName, StringComparison.OrdinalIgnoreCase))
        {
            _pendingSceneName = null;
        }
    }

    private static void RestoreInventory(Player player, List<SavedSlot> slots, bool allowRemoteRpc)
    {
        if (player.itemSlots == null)
        {
            return;
        }

        var photonView = player.photonView;
        if (photonView == null)
        {
            Log.LogWarning("Cannot restore inventory: target player photonView missing.");
            return;
        }

        if (!photonView.IsMine)
        {
            if (!allowRemoteRpc)
            {
                Log.LogWarning($"Skipping remote inventory restore for actor {photonView.OwnerActorNr}. Enable ExperimentalRemoteInventoryRestore to force it.");
                return;
            }

            try
            {
                var remotePayload = BuildInventoryPayload(player.itemSlots.Length, slots);
                photonView.RPC("RPC_SetInventory", RpcTarget.All, remotePayload);
            }
            catch (Exception ex)
            {
                Log.LogError($"Remote inventory RPC restore failed for actor {photonView.OwnerActorNr}: {ex.Message}");
            }

            return;
        }

        for (var i = 0; i < player.itemSlots.Length; i++)
        {
            player.RPCRemoveItemFromSlot((byte)i);
        }

        if (Player.BACKPACKSLOTINDEX >= 0 && Player.BACKPACKSLOTINDEX <= byte.MaxValue)
        {
            player.RPCRemoveItemFromSlot((byte)Player.BACKPACKSLOTINDEX);
        }

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (!slot.hasItem)
            {
                continue;
            }

            _ = player.AddItem(slot.itemId, new ItemInstanceData(), out _);
        }
    }

    private static byte[] BuildInventoryPayload(int expectedSlotCount, List<SavedSlot> slots)
    {
        if (expectedSlotCount <= 0)
        {
            return Array.Empty<byte>();
        }

        var itemSlots = new ItemSlot[expectedSlotCount];
        for (var i = 0; i < expectedSlotCount; i++)
        {
            itemSlots[i] = new ItemSlot((byte)i);
        }

        var max = Mathf.Min(expectedSlotCount, slots.Count);
        for (var i = 0; i < max; i++)
        {
            var saved = slots[i];
            if (!saved.hasItem)
            {
                continue;
            }

            if (ItemDatabase.TryGetItem(saved.itemId, out var item))
            {
                itemSlots[i].prefab = item;
            }
            itemSlots[i].data = new ItemInstanceData();
        }

        var backpackId = Player.BACKPACKSLOTINDEX >= 0 && Player.BACKPACKSLOTINDEX <= byte.MaxValue
            ? (byte)Player.BACKPACKSLOTINDEX
            : (byte)127;
        var backpackSlot = new BackpackSlot(backpackId)
        {
            hasBackpack = false,
            prefab = null,
            data = new ItemInstanceData()
        };

        var tempSlot = new ItemSlot(126)
        {
            prefab = null,
            data = new ItemInstanceData()
        };

        var syncData = new InventorySyncData(itemSlots, backpackSlot, tempSlot);
        using var serializer = new BinarySerializer();
        syncData.Serialize(serializer);

        var payload = new byte[serializer.Position];
        for (var i = 0; i < serializer.Position; i++)
        {
            payload[i] = serializer.buffer[i];
        }

        return payload;
    }

    private static void RestoreHealth(Character? character, float passOutValue, float stamina, bool hasHealth)
    {
        if (!hasHealth || character == null)
        {
            return;
        }

        var data = character.data;
        if (data == null)
        {
            return;
        }

        if (data.dead && character.IsLocal)
        {
            Character.Revive();
            data.dead = false;
        }

        data.passOutValue = passOutValue;
        data.currentStamina = Mathf.Clamp(stamina, 0f, data.TotalStamina);
    }

    private static List<PlayerSnapshot> GetSnapshotPlayers(RunSnapshot snapshot)
    {
        if (snapshot.players != null && snapshot.players.Count > 0)
        {
            foreach (var player in snapshot.players)
            {
                HydratePlayerSlots(player);
            }

            return snapshot.players;
        }

        if (snapshot.playersArray != null && snapshot.playersArray.Length > 0)
        {
            var list = new List<PlayerSnapshot>(snapshot.playersArray);
            foreach (var player in list)
            {
                HydratePlayerSlots(player);
            }

            return list;
        }

        return new List<PlayerSnapshot>();
    }

    private static void HydratePlayerSlots(PlayerSnapshot player)
    {
        if (player.slots != null && player.slots.Count > 0)
        {
            return;
        }

        if (player.slotsArray != null && player.slotsArray.Length > 0)
        {
            player.slots = new List<SavedSlot>(player.slotsArray);
            return;
        }

        player.slots = new List<SavedSlot>();
    }

    private static List<SavedSlot> GetSnapshotSlots(RunSnapshot snapshot)
    {
        if (snapshot.slots != null && snapshot.slots.Count > 0)
        {
            return snapshot.slots;
        }

        if (snapshot.slotsArray != null && snapshot.slotsArray.Length > 0)
        {
            return new List<SavedSlot>(snapshot.slotsArray);
        }

        return new List<SavedSlot>();
    }
}
