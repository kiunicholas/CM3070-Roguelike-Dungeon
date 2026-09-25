using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

/*
 * ProceduralDungeonPrototype.cs
 * Controls: WASD / Arrow Keys to move, R to regenerate, T to run 100 dungeon evaluation tests.
 *
 * This prototype demonstrates:
 * - Constructive room-based procedural dungeon generation
 * - Grid / graph representation of rooms
 * - Reachability validation using BFS
 * - Runtime Unity Tilemap rendering
 * - Player movement, enemies, loot, exit trigger
 * - Evaluation logging for validity, generation time, path length, variation, and fairness
 */
public class ProceduralDungeonPrototype : MonoBehaviour
{
    [Header("Dungeon Generation")]
    [Min(6)] public int targetRoomCount = 18;
    [Min(3)] public int gridRadius = 6;
    public Vector2Int roomSize = new Vector2Int(8, 6);
    [Range(1, 4)] public int corridorWidth = 2;
    [Range(0f, 1f)] public float extraLoopChance = 0.25f;
    [Min(0)] public int minimumExitDistance = 4;

    [Header("Gameplay Content")]
    [Min(0)] public int enemyCount = 7;
    [Min(0)] public int lootCount = 6;
    [Min(0)] public int minimumEnemyDistanceFromStart = 2;
    public float playerMoveSpeed = 5f;

    [Header("Player Health")]
    [Min(1)] public int maxPlayerHealth = 3;
    public float enemyDamageCooldown = 1f;

    [Header("Evaluation")]
    [Min(1)] public int batchTestCount = 100;
    public bool runBatchTestOnStart = false;
    public bool printCsvRowsToConsole = false;

    [Header("Random Seed")]
    public bool useRandomSeed = true;
    public int fixedSeed = 12345;

    [Header("Tile Colours")]
    public Color floorColor = new Color(0.24f, 0.24f, 0.28f);
    public Color wallColor = new Color(0.08f, 0.08f, 0.10f);
    public Color playerColor = new Color(0.25f, 0.65f, 1f);
    public Color enemyColor = new Color(0.90f, 0.18f, 0.18f);
    public Color lootColor = new Color(1f, 0.85f, 0.20f);
    public Color exitColor = new Color(0.25f, 1f, 0.45f);

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    private GameObject generatedRoot;
    private Tilemap floorTilemap;
    private Tilemap wallTilemap;
    private Tile floorTile;
    private Tile wallTile;
    private Sprite squareSprite;
    private Text debugText;
    private float nextDebugUiRefreshTime;
    private const float DebugUiRefreshInterval = 0.35f;
    private DungeonResult currentDungeon;
    private int generationNumber;
    private int collectedLoot;
    private bool playerReachedExit;
    private bool playerDefeated;
    private int currentPlayerHealth;
    private float nextEnemyDamageTime;

    private void Start()
    {
        // Low-lag prototype settings: disable VSync override, target a higher frame rate,
        // and make physics update at 60 Hz for more responsive movement.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 120;
        Time.fixedDeltaTime = 1f / 60f;

        CreateRuntimeAssets();
        CreateDebugUI();
        GenerateAndBuildDungeon();

        if (runBatchTestOnStart)
        {
            RunBatchEvaluation();
        }
    }

    private void Update()
    {
        if (WasRegeneratePressed())
        {
            GenerateAndBuildDungeon();
        }

        if (WasBatchTestPressed())
        {
            RunBatchEvaluation();
        }

        if (Time.unscaledTime >= nextDebugUiRefreshTime)
        {
            nextDebugUiRefreshTime = Time.unscaledTime + DebugUiRefreshInterval;
            RefreshDebugText();
        }
    }

    private bool WasRegeneratePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.R))
        {
            return true;
        }
#endif

        return false;
    }

    private bool WasBatchTestPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.tKey.wasPressedThisFrame)
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.T))
        {
            return true;
        }
#endif

        return false;
    }

    public void GenerateAndBuildDungeon()
    {
        int seed = useRandomSeed ? UnityEngine.Random.Range(1, int.MaxValue) : fixedSeed + generationNumber;
        currentDungeon = GenerateDungeonData(seed);
        generationNumber++;
        collectedLoot = 0;
        playerReachedExit = false;
        playerDefeated = false;
        currentPlayerHealth = maxPlayerHealth;
        nextEnemyDamageTime = 0f;

        ClearGeneratedObjects();
        BuildTilemapVisuals(currentDungeon);
        SpawnGameplayObjects(currentDungeon);
        PositionCamera(currentDungeon);
        RefreshDebugText();

        Debug.Log($"Generated dungeon #{generationNumber}: seed={seed}, rooms={currentDungeon.rooms.Count}, valid={currentDungeon.valid}, exitDistance={currentDungeon.exitDistance}, generationMs={currentDungeon.generationMs:F3}");
    }
   
    private DungeonResult GenerateDungeonData(int seed)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        System.Random rng = new System.Random(seed);

        DungeonResult result = new DungeonResult();
        result.seed = seed;
        result.startIndex = 0;
        result.rooms = new List<RoomNode>();

        Dictionary<Vector2Int, int> indexByPosition = new Dictionary<Vector2Int, int>();

        int AddRoom(Vector2Int gridPosition)
        {
            RoomNode room = new RoomNode();
            room.gridPosition = gridPosition;
            result.rooms.Add(room);
            int index = result.rooms.Count - 1;
            indexByPosition[gridPosition] = index;
            return index;
        }

        void ConnectRooms(int a, int b)
        {
            if (a == b) return;
            result.rooms[a].connections.Add(b);
            result.rooms[b].connections.Add(a);
        }

        AddRoom(Vector2Int.zero);

        int attempts = 0;
        int maxAttempts = targetRoomCount * 300;

        while (result.rooms.Count < targetRoomCount && attempts < maxAttempts)
        {
            attempts++;
            int fromIndex = rng.Next(result.rooms.Count);
            RoomNode fromRoom = result.rooms[fromIndex];
            Vector2Int direction = Directions[rng.Next(Directions.Length)];
            Vector2Int newGridPosition = fromRoom.gridPosition + direction;

            if (Mathf.Abs(newGridPosition.x) > gridRadius || Mathf.Abs(newGridPosition.y) > gridRadius)
            {
                continue;
            }

            if (indexByPosition.TryGetValue(newGridPosition, out int existingIndex))
            {
               
                if (rng.NextDouble() < extraLoopChance)
                {
                    ConnectRooms(fromIndex, existingIndex);
                }
                continue;
            }

            int newIndex = AddRoom(newGridPosition);
            ConnectRooms(fromIndex, newIndex);
        }

        int[] distances = CalculateDistances(result.rooms, result.startIndex);
        for (int i = 0; i < result.rooms.Count; i++)
        {
            result.rooms[i].distanceFromStart = distances[i];
        }

        result.exitIndex = SelectExitRoom(result.rooms, distances, rng);
        result.exitDistance = distances[result.exitIndex];
        result.branchCount = CountBranches(result.rooms);
        result.valid = IsReachable(result.rooms, result.startIndex, result.exitIndex)
                       && result.rooms.Count >= Mathf.Max(6, targetRoomCount / 2)
                       && result.exitDistance >= Mathf.Min(minimumExitDistance, Mathf.Max(1, result.rooms.Count / 4));

        stopwatch.Stop();
        result.generationMs = stopwatch.Elapsed.TotalMilliseconds;
        return result;
    }

    private int SelectExitRoom(List<RoomNode> rooms, int[] distances, System.Random rng)
    {
        int bestDistance = -1;
        List<int> candidates = new List<int>();

        for (int i = 0; i < rooms.Count; i++)
        {
            if (i == 0 || distances[i] < 0) continue;

            if (distances[i] > bestDistance)
            {
                candidates.Clear();
                bestDistance = distances[i];
                candidates.Add(i);
            }
            else if (distances[i] == bestDistance)
            {
                candidates.Add(i);
            }
        }

        if (candidates.Count == 0) return 0;
        return candidates[rng.Next(candidates.Count)];
    }

    private int[] CalculateDistances(List<RoomNode> rooms, int startIndex)
    {
        int[] distances = new int[rooms.Count];
        for (int i = 0; i < distances.Length; i++) distances[i] = -1;

        Queue<int> queue = new Queue<int>();
        distances[startIndex] = 0;
        queue.Enqueue(startIndex);

        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            foreach (int neighbour in rooms[current].connections)
            {
                if (distances[neighbour] >= 0) continue;
                distances[neighbour] = distances[current] + 1;
                queue.Enqueue(neighbour);
            }
        }

        return distances;
    }

    private bool IsReachable(List<RoomNode> rooms, int startIndex, int exitIndex)
    {
        int[] distances = CalculateDistances(rooms, startIndex);
        return exitIndex >= 0 && exitIndex < distances.Length && distances[exitIndex] >= 0;
    }

    private int CountBranches(List<RoomNode> rooms)
    {
        int branchCount = 0;
        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i].connections.Count >= 3) branchCount++;
        }
        return branchCount;
    }

    private void BuildTilemapVisuals(DungeonResult dungeon)
    {
        generatedRoot = new GameObject("Generated Dungeon");

        GameObject gridObject = new GameObject("Grid");
        gridObject.transform.SetParent(generatedRoot.transform);
        gridObject.AddComponent<Grid>();

        GameObject floorObject = new GameObject("Floor Tilemap");
        floorObject.transform.SetParent(gridObject.transform);
        floorTilemap = floorObject.AddComponent<Tilemap>();
        floorObject.AddComponent<TilemapRenderer>();

        GameObject wallObject = new GameObject("Wall Tilemap");
        wallObject.transform.SetParent(gridObject.transform);
        wallTilemap = wallObject.AddComponent<Tilemap>();
        wallObject.AddComponent<TilemapRenderer>();

        TilemapCollider2D wallCollider = wallObject.AddComponent<TilemapCollider2D>();
        wallCollider.compositeOperation = Collider2D.CompositeOperation.Merge;

        Rigidbody2D wallBody = wallObject.AddComponent<Rigidbody2D>();
        wallBody.bodyType = RigidbodyType2D.Static;

        wallObject.AddComponent<CompositeCollider2D>();

        HashSet<Vector3Int> floorCells = new HashSet<Vector3Int>();
        HashSet<Vector3Int> wallCells = new HashSet<Vector3Int>();

        foreach (RoomNode room in dungeon.rooms)
        {
            AddRoomFloorCells(floorCells, GetRoomCentreCell(room.gridPosition));
        }

        for (int i = 0; i < dungeon.rooms.Count; i++)
        {
            foreach (int connectedIndex in dungeon.rooms[i].connections)
            {
                if (connectedIndex <= i) continue;
                AddCorridorFloorCells(floorCells, GetRoomCentreCell(dungeon.rooms[i].gridPosition), GetRoomCentreCell(dungeon.rooms[connectedIndex].gridPosition));
            }
        }

        foreach (Vector3Int floor in floorCells)
        {
            floorTilemap.SetTile(floor, floorTile);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector3Int neighbour = new Vector3Int(floor.x + x, floor.y + y, 0);
                    if (!floorCells.Contains(neighbour))
                    {
                        wallCells.Add(neighbour);
                    }
                }
            }
        }

        foreach (Vector3Int wall in wallCells)
        {
            if (!floorCells.Contains(wall))
            {
                wallTilemap.SetTile(wall, wallTile);
            }
        }
    }

    private void AddRoomFloorCells(HashSet<Vector3Int> floorCells, Vector3Int centre)
    {
        int halfWidth = Mathf.Max(2, roomSize.x / 2);
        int halfHeight = Mathf.Max(2, roomSize.y / 2);

        for (int x = -halfWidth; x <= halfWidth; x++)
        {
            for (int y = -halfHeight; y <= halfHeight; y++)
            {
                floorCells.Add(new Vector3Int(centre.x + x, centre.y + y, 0));
            }
        }
    }

    private void AddCorridorFloorCells(HashSet<Vector3Int> floorCells, Vector3Int from, Vector3Int to)
    {
        int thickness = Mathf.Max(1, corridorWidth);
        int halfThickness = thickness / 2;

        int minX = Mathf.Min(from.x, to.x);
        int maxX = Mathf.Max(from.x, to.x);
        for (int x = minX; x <= maxX; x++)
        {
            for (int offset = -halfThickness; offset <= halfThickness; offset++)
            {
                floorCells.Add(new Vector3Int(x, from.y + offset, 0));
            }
        }

        int minY = Mathf.Min(from.y, to.y);
        int maxY = Mathf.Max(from.y, to.y);
        for (int y = minY; y <= maxY; y++)
        {
            for (int offset = -halfThickness; offset <= halfThickness; offset++)
            {
                floorCells.Add(new Vector3Int(to.x + offset, y, 0));
            }
        }
    }

    private Vector3Int GetRoomCentreCell(Vector2Int gridPosition)
    {
        int spacingX = roomSize.x + 5;
        int spacingY = roomSize.y + 5;
        return new Vector3Int(gridPosition.x * spacingX, gridPosition.y * spacingY, 0);
    }

    private Vector3 GetRoomWorldCentre(RoomNode room)
    {
        Vector3Int centreCell = GetRoomCentreCell(room.gridPosition);
        return new Vector3(centreCell.x + 0.5f, centreCell.y + 0.5f, 0f);
    }

    private void SpawnGameplayObjects(DungeonResult dungeon)
    {
        GameObject gameplayRoot = new GameObject("Gameplay Objects");
        gameplayRoot.transform.SetParent(generatedRoot.transform);

        SpawnPlayer(dungeon, gameplayRoot.transform);
        SpawnExit(dungeon, gameplayRoot.transform);
        SpawnEnemiesAndLoot(dungeon, gameplayRoot.transform);
    }

    private void SpawnPlayer(DungeonResult dungeon, Transform parent)
    {
        EnsureCameraExists();

        Vector3 spawnPosition = GetRoomWorldCentre(dungeon.rooms[dungeon.startIndex]);
        GameObject player = CreateColouredObject("Player", playerColor, spawnPosition, 0.72f, parent);
        player.tag = "Player";

        Rigidbody2D body = player.AddComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.sleepMode = RigidbodySleepMode2D.NeverSleep;
        BoxCollider2D collider = player.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one * 0.72f;

        PlayerMover mover = player.AddComponent<PlayerMover>();
        mover.speed = playerMoveSpeed;

        CameraFollow follow = Camera.main.gameObject.GetComponent<CameraFollow>();
        if (follow == null) follow = Camera.main.gameObject.AddComponent<CameraFollow>();
        follow.target = player.transform;
    }

    private void SpawnExit(DungeonResult dungeon, Transform parent)
    {
        Vector3 exitPosition = GetRoomWorldCentre(dungeon.rooms[dungeon.exitIndex]);
        GameObject exit = CreateColouredObject("Exit", exitColor, exitPosition, 1f, parent);
        BoxCollider2D trigger = exit.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        ExitTrigger exitTrigger = exit.AddComponent<ExitTrigger>();
        exitTrigger.prototype = this;
    }

    private void SpawnEnemiesAndLoot(DungeonResult dungeon, Transform parent)
    {
        System.Random rng = new System.Random(dungeon.seed + 999);
        List<int> validEnemyRooms = new List<int>();
        List<int> validLootRooms = new List<int>();

        for (int i = 0; i < dungeon.rooms.Count; i++)
        {
            if (i == dungeon.startIndex || i == dungeon.exitIndex) continue;

            if (dungeon.rooms[i].distanceFromStart >= minimumEnemyDistanceFromStart)
            {
                validEnemyRooms.Add(i);
            }

            if (dungeon.rooms[i].distanceFromStart >= 1)
            {
                validLootRooms.Add(i);
            }
        }

        Shuffle(validEnemyRooms, rng);
        Shuffle(validLootRooms, rng);

        int enemiesToSpawn = Mathf.Min(enemyCount, validEnemyRooms.Count);
        for (int i = 0; i < enemiesToSpawn; i++)
        {
            Vector3 enemyPosition = GetRoomWorldCentre(dungeon.rooms[validEnemyRooms[i]]) + RandomOffsetInRoom(rng, 2f);
            GameObject enemy = CreateColouredObject("Enemy", enemyColor, enemyPosition, 0.7f, parent);
            BoxCollider2D trigger = enemy.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            EnemyPatrol patrol = enemy.AddComponent<EnemyPatrol>();
            patrol.origin = enemyPosition;
            patrol.prototype = this;
        }

        int lootToSpawn = Mathf.Min(lootCount, validLootRooms.Count);
        for (int i = 0; i < lootToSpawn; i++)
        {
            Vector3 lootPosition = GetRoomWorldCentre(dungeon.rooms[validLootRooms[i]]) + RandomOffsetInRoom(rng, 2.2f);
            GameObject loot = CreateColouredObject("Loot", lootColor, lootPosition, 0.55f, parent);
            BoxCollider2D trigger = loot.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            LootPickup pickup = loot.AddComponent<LootPickup>();
            pickup.prototype = this;
        }
    }

    private Vector3 RandomOffsetInRoom(System.Random rng, float maxDistance)
    {
        float x = (float)(rng.NextDouble() * 2.0 - 1.0) * maxDistance;
        float y = (float)(rng.NextDouble() * 2.0 - 1.0) * maxDistance;
        return new Vector3(x, y, 0f);
    }

    private void Shuffle(List<int> values, System.Random rng)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            int temp = values[i];
            values[i] = values[j];
            values[j] = temp;
        }
    }

    private GameObject CreateColouredObject(string objectName, Color color, Vector3 position, float scale, Transform parent)
    {
        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(parent);
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * scale;

        SpriteRenderer renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = squareSprite;
        renderer.color = color;
        renderer.sortingOrder = 10;
        return obj;
    }

    private void CreateRuntimeAssets()
    {
        squareSprite = CreateSquareSprite(Color.white);

        floorTile = ScriptableObject.CreateInstance<Tile>();
        floorTile.sprite = CreateSquareSprite(floorColor);
        floorTile.color = Color.white;

        wallTile = ScriptableObject.CreateInstance<Tile>();
        wallTile.sprite = CreateSquareSprite(wallColor);
        wallTile.color = Color.white;
    }

    private Sprite CreateSquareSprite(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.filterMode = FilterMode.Point;
        texture.Apply();
        return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    private void PositionCamera(DungeonResult dungeon)
    {
        EnsureCameraExists();

        Camera.main.orthographic = true;
        Camera.main.orthographicSize = 9f;
        Camera.main.transform.position = GetRoomWorldCentre(dungeon.rooms[dungeon.startIndex]) + new Vector3(0, 0, -10f);
    }


    private void EnsureCameraExists()
    {
        if (Camera.main != null) return;

        GameObject cameraObject = new GameObject("Main Camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 0, -10f);
    }

    private void CreateDebugUI()
    {
        Canvas existingCanvas = FindAnyObjectByType<Canvas>();
        GameObject canvasObject;

        if (existingCanvas == null)
        {
            canvasObject = new GameObject("Prototype UI Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<GraphicRaycaster>();
        }
        else
        {
            canvasObject = existingCanvas.gameObject;
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            scaler = canvasObject.AddComponent<CanvasScaler>();
        }

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GameObject textObject = new GameObject("Prototype Debug Text");
        textObject.transform.SetParent(canvasObject.transform, false);

        debugText = textObject.AddComponent<Text>();
        debugText.font = GetBuiltInUIFont();
        debugText.fontSize = 28;
        debugText.alignment = TextAnchor.UpperLeft;
        debugText.color = Color.white;
        debugText.raycastTarget = false;
        debugText.horizontalOverflow = HorizontalWrapMode.Wrap;
        debugText.verticalOverflow = VerticalWrapMode.Overflow;

        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);

        RectTransform rect = debugText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(24, -24);
        rect.sizeDelta = new Vector2(1050, 420);
    }


    private Font GetBuiltInUIFont()
    {
        try
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) return font;
        }
        catch (Exception)
        {
            // Ignore and try older fallback below.
        }

        try
        {
            Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font != null) return font;
        }
        catch (Exception)
        {
            // Ignore and try operating system font fallback below.
        }

        try
        {
            return Font.CreateDynamicFontFromOSFont("Arial", 16);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RefreshDebugText()
    {
        if (debugText == null || currentDungeon == null) return;

        string status = playerDefeated ? "DEFEATED" : playerReachedExit ? "EXIT REACHED" : "Exploring";
        debugText.text =
            "Procedural Dungeon Prototype - Unity/C#\n" +
            "Controls: WASD/Arrow Keys = Move | R = Regenerate | T = Run 100-test evaluation\n\n" +
            $"Status: {status}\n" +
            $"Dungeon #{generationNumber} | Seed: {currentDungeon.seed}\n" +
            $"Rooms: {currentDungeon.rooms.Count}/{targetRoomCount} | Branch Rooms: {currentDungeon.branchCount}\n" +
            $"Valid: {currentDungeon.valid} | Exit Reachable: {currentDungeon.exitDistance >= 0}\n" +
            $"Start-to-Exit Distance: {currentDungeon.exitDistance} rooms\n" +
            $"Generation Time: {currentDungeon.generationMs:F3} ms\n" +
            $"Enemies: {enemyCount} | Loot Collected: {collectedLoot}/{Mathf.Min(lootCount, Mathf.Max(0, currentDungeon.rooms.Count - 2))}\n" +
            $"Player Health: {currentPlayerHealth}/{maxPlayerHealth}";
    }

    private void ClearGeneratedObjects()
    {
        if (generatedRoot != null)
        {
            if (Application.isPlaying) Destroy(generatedRoot);
            else DestroyImmediate(generatedRoot);
        }
    }

    public void RegisterLootCollected(GameObject loot)
    {
        collectedLoot++;
        Destroy(loot);
        RefreshDebugText();
    }

    public void RegisterExitReached()
    {
        if (playerReachedExit) return;
        playerReachedExit = true;
        Debug.Log("Player reached the exit. Prototype success condition achieved.");
        RefreshDebugText();
    }

    public void RegisterPlayerHit()
    {
        if (playerReachedExit || playerDefeated) return;

        if (Time.time < nextEnemyDamageTime)
        {
            return;
        }

        nextEnemyDamageTime = Time.time + enemyDamageCooldown;
        currentPlayerHealth = Mathf.Max(0, currentPlayerHealth - 1);

        Debug.Log($"Player hit by enemy. Health: {currentPlayerHealth}/{maxPlayerHealth}");

        if (currentPlayerHealth <= 0)
        {
            playerDefeated = true;
            Debug.Log("Player defeated. Press R to generate a new dungeon and try again.");
        }

        RefreshDebugText();
    }

    public void RunBatchEvaluation()
    {
        int validCount = 0;
        double totalMs = 0;
        int totalRooms = 0;
        int totalExitDistance = 0;
        int minExitDistanceObserved = int.MaxValue;
        int maxExitDistanceObserved = int.MinValue;
        int totalBranches = 0;

        if (printCsvRowsToConsole)
        {
            Debug.Log("seed,valid,rooms,exitDistance,branchRooms,generationMs");
        }

        for (int i = 0; i < batchTestCount; i++)
        {
            int seed = useRandomSeed ? UnityEngine.Random.Range(1, int.MaxValue) : fixedSeed + 100000 + i;
            DungeonResult result = GenerateDungeonData(seed);

            if (result.valid) validCount++;
            totalMs += result.generationMs;
            totalRooms += result.rooms.Count;
            totalExitDistance += result.exitDistance;
            totalBranches += result.branchCount;
            minExitDistanceObserved = Mathf.Min(minExitDistanceObserved, result.exitDistance);
            maxExitDistanceObserved = Mathf.Max(maxExitDistanceObserved, result.exitDistance);

            if (printCsvRowsToConsole)
            {
                Debug.Log($"{result.seed},{result.valid},{result.rooms.Count},{result.exitDistance},{result.branchCount},{result.generationMs:F3}");
            }
        }

        float validityRate = (float)validCount / Mathf.Max(1, batchTestCount) * 100f;
        double averageMs = totalMs / Mathf.Max(1, batchTestCount);
        float averageRooms = (float)totalRooms / Mathf.Max(1, batchTestCount);
        float averageExitDistance = (float)totalExitDistance / Mathf.Max(1, batchTestCount);
        float averageBranches = (float)totalBranches / Mathf.Max(1, batchTestCount);

        Debug.Log(
            "BATCH EVALUATION SUMMARY\n" +
            $"Tests: {batchTestCount}\n" +
            $"Valid Dungeon Rate: {validCount}/{batchTestCount} = {validityRate:F1}%\n" +
            $"Average Generation Time: {averageMs:F3} ms\n" +
            $"Average Room Count: {averageRooms:F2}\n" +
            $"Average Exit Distance: {averageExitDistance:F2} rooms\n" +
            $"Exit Distance Range: {minExitDistanceObserved} to {maxExitDistanceObserved} rooms\n" +
            $"Average Branch Rooms: {averageBranches:F2}\n" +
            "Use these values in Chapter 4 under prototype evaluation."
        );
    }

    private class RoomNode
    {
        public Vector2Int gridPosition;
        public HashSet<int> connections = new HashSet<int>();
        public int distanceFromStart;
    }

    private class DungeonResult
    {
        public int seed;
        public int startIndex;
        public int exitIndex;
        public int exitDistance;
        public int branchCount;
        public bool valid;
        public double generationMs;
        public List<RoomNode> rooms;
    }
}

public class PlayerMover : MonoBehaviour
{
    public float speed = 5f;
    private Rigidbody2D body;
    private Vector2 input;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }
    }

    private void Update()
    {
        input = ReadMovementInput();
        input = input.sqrMagnitude > 1f ? input.normalized : input;

        // Apply velocity immediately in Update as well as FixedUpdate.
        // This makes the prototype feel responsive in the Unity Editor,
        // especially when the Editor frame rate is unstable.
        ApplyVelocity();
    }

    private Vector2 ReadMovementInput()
    {
        Vector2 movement = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.y += 1f;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (movement == Vector2.zero)
        {
            movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }
#endif

        return movement;
    }

    private void FixedUpdate()
    {
        ApplyVelocity();
    }

    private void ApplyVelocity()
    {
        if (body == null) return;
        body.linearVelocity = input * speed;
    }
}

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public bool snapToPlayer = true;
    public float smoothSpeed = 16f;

    private void LateUpdate()
    {
        if (target == null) return;
        Vector3 targetPosition = new Vector3(target.position.x, target.position.y, -10f);

        // Snapping removes the delayed-camera feeling during testing.
        // Set snapToPlayer to false in the Inspector if you want a smoother camera later.
        if (snapToPlayer)
        {
            transform.position = targetPosition;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * smoothSpeed);
        }
    }
}

public class EnemyPatrol : MonoBehaviour
{
    public ProceduralDungeonPrototype prototype;
    public Vector3 origin;
    public float patrolRadius = 1.4f;
    public float patrolSpeed = 1.2f;

    private void Update()
    {
        Vector3 offset = new Vector3(
            Mathf.Sin(Time.time * patrolSpeed),
            Mathf.Cos(Time.time * patrolSpeed * 0.7f),
            0f
        ) * patrolRadius;

        transform.position = origin + offset;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryDamagePlayer(other);
    }

    private void TryDamagePlayer(Collider2D other)
    {
        if (other.GetComponent<PlayerMover>() == null) return;

        if (prototype != null)
        {
            prototype.RegisterPlayerHit();
        }
    }
}

public class LootPickup : MonoBehaviour
{
    public ProceduralDungeonPrototype prototype;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerMover>() == null) return;
        if (prototype != null) prototype.RegisterLootCollected(gameObject);
    }
}

public class ExitTrigger : MonoBehaviour
{
    public ProceduralDungeonPrototype prototype;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerMover>() == null) return;
        if (prototype != null) prototype.RegisterExitReached();
    }
}
