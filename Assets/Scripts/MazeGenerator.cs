using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;


public class Cell
{
    public bool visited = false;
    public bool[] walls = { true, true, true, true }; // North, East, South, West
}

public class MazeGenerator : NetworkBehaviour
{
    public int minSize = 4; // 4x4
    public int maxSize = 12; // 12x12
    public float cellSize = 1.0f;
    public float wallRemovalPercentage = 0.2f;

    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject cornerPrefab;
    public Transform mazeParent;

    private GameManager gameManager;
    private int width;
    private int height;
    private Cell[,] grid;
    private Stack<Vector2Int> stack = new Stack<Vector2Int>();
    private List<Vector2Int> availableCellsList;
    private bool publishedAvailableCellsToGameManager;

    public override void OnNetworkSpawn()
    {
        // Only the dedicated server or host will generate the maze and sync to clients
        if (IsServer)
        {
            Debug.Log("[Server] Generating initial maze...");
            RegenerateMaze();
        }
        else
        {
            Debug.Log("[Client] Waiting for maze data from server...");
            RequestMazeDataServerRpc();
        }
    }

    private void Update()
    {
        if (!IsServer || publishedAvailableCellsToGameManager || availableCellsList == null)
        {
            return;
        }

        TryPublishAvailableCells();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestMazeDataServerRpc(ServerRpcParams serverRpcParams = default)
    {
        if (!IsServer)
        {
            return;
        }

        ulong requesterClientId = serverRpcParams.Receive.SenderClientId;
        SendMazeDataToClient(requesterClientId);
    }

    public void RegenerateMaze()
    {
        Debug.Log("[MazeGenerator] Regenerating maze...");
        publishedAvailableCellsToGameManager = false;
        GenerateRandomDimensions();
        GenerateMaze();
        RemoveRandomWalls();
        DrawMaze();
        InitializeAvailableCells();
        Shuffle(availableCellsList);
        TryPublishAvailableCells();

        // Sync the new maze to all clients
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            StartCoroutine(SyncMazeAfterDelay());
        }
        else
        {
            Debug.LogError("[MazeGenerator] NetworkObject is not spawned. Cannot send ClientRpc.");
        }
    }
    
    void GenerateRandomDimensions()
    {
        int newWidth, newHeight;
        int attempt = 0;
        int maxAttempts = 100; // Prevents potential infinite loops

        do
        {
            newWidth = Random.Range(minSize, maxSize + 1);
            newHeight = Random.Range(minSize, maxSize + 1);
            attempt++;

            // Prevent width and height from being at extreme opposites
            if (!((newWidth == maxSize && newHeight == minSize) ||
                  (newWidth == minSize && newHeight == maxSize)))
            {
                break;
            }

        } while (attempt < maxAttempts);

        // Fallback in case no valid dimensions are found within the attempts
        if (attempt == maxAttempts)
        {
            Debug.LogWarning("[MazeGenerator] Failed to generate valid dimensions within attempts. Using default values.");
            newWidth = Mathf.Clamp(newWidth, minSize, maxSize);
            newHeight = Mathf.Clamp(newHeight, minSize, maxSize);
        }

        width = newWidth;
        height = newHeight;

        Debug.Log($"[MazeGenerator] Generated maze dimensions: {width}x{height}");
    }

    IEnumerator SyncMazeAfterDelay()
    {
        yield return new WaitForSeconds(0.5f); // Adjust delay as needed
        SyncMazeToAllClients();
    }

    private void SyncMazeToAllClients()
    {
        int[] data = SerializeMazeData();
        SyncMazeDataToClientClientRpc(data);
    }

    private void InitializeAvailableCells()
    {
        availableCellsList = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                availableCellsList.Add(new Vector2Int(x, y));
            }
        }
    }
    
    private void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void TryPublishAvailableCells()
    {
        if (availableCellsList == null)
        {
            return;
        }

        if (gameManager == null)
        {
            gameManager = FindFirstObjectByType<GameManager>();
        }

        if (gameManager != null)
        {
            gameManager.SetAvailableCells(new List<Vector2Int>(availableCellsList));
            publishedAvailableCellsToGameManager = true;
            Debug.Log($"[MazeGenerator] Published {availableCellsList.Count} available cells to GameManager.");
        }
        else
        {
            Debug.Log("[MazeGenerator] GameManager not ready yet. Will retry publishing available cells.");
        }
    }

    public void SendMazeDataToClient(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }

        if (grid == null || width <= 0 || height <= 0)
        {
            Debug.LogWarning("[MazeGenerator] Maze data requested before generation. Regenerating now.");
            RegenerateMaze();
        }

        Debug.Log($"[Server] Sending maze data to client {clientId}...");

        int[] data = SerializeMazeData();

        SyncMazeDataToClientClientRpc(
            data, 
            new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            }
        );
    }

    [ClientRpc]
    private void SyncMazeDataToClientClientRpc(int[] serializedData, ClientRpcParams clientRpcParams = default)
    {
        if (!IsClient)
        {
            return;
        }

        if (serializedData == null || serializedData.Length == 0)
        {
            Debug.LogWarning("[MazeGenerator] Received empty maze data payload.");
            return;
        }

        ulong localClientId = NetworkManager != null ? NetworkManager.LocalClientId : ulong.MaxValue;
        Debug.Log($"[Client {localClientId}] Received maze data. Deserializing...");
        DeserializeMazeData(serializedData);
    }

    public Vector3 CellToWorldPosition(Vector2Int cell)
    {
        // Calculate the maze's dimensions in world units
        float mazeWidth = width * cellSize;
        float mazeHeight = height * cellSize;

        // Calculate offsets to center the maze
        float offsetX = -mazeWidth / 2 + cellSize / 2;
        float offsetY = -mazeHeight / 2 + cellSize / 2;

        // Compute the world position based on cell coordinates and offsets
        float x = cell.x * cellSize + offsetX;
        float y = cell.y * cellSize + offsetY;

        return new Vector3(x, y, 0);
    }

    public bool TryWorldToCell(Vector2 worldPosition, out Vector2Int cell)
    {
        cell = default;
        if (grid == null || width <= 0 || height <= 0 || cellSize <= 0f)
        {
            return false;
        }

        float mazeWidth = width * cellSize;
        float mazeHeight = height * cellSize;
        float minX = -mazeWidth * 0.5f;
        float minY = -mazeHeight * 0.5f;

        int cellX = Mathf.FloorToInt((worldPosition.x - minX) / cellSize);
        int cellY = Mathf.FloorToInt((worldPosition.y - minY) / cellSize);
        Vector2Int candidate = new Vector2Int(cellX, cellY);
        if (!IsCellInBounds(candidate))
        {
            return false;
        }

        cell = candidate;
        return true;
    }

    public bool TryFindPath(Vector2 startWorldPosition, Vector2 goalWorldPosition, List<Vector2> worldPath)
    {
        if (worldPath == null)
        {
            return false;
        }

        worldPath.Clear();
        if (grid == null || width <= 0 || height <= 0)
        {
            return false;
        }

        if (!TryWorldToCell(startWorldPosition, out Vector2Int startCell) ||
            !TryWorldToCell(goalWorldPosition, out Vector2Int goalCell))
        {
            return false;
        }

        if (startCell == goalCell)
        {
            worldPath.Add(CellToWorldPosition(goalCell));
            return true;
        }

        List<Vector2Int> openSet = new List<Vector2Int> { startCell };
        HashSet<Vector2Int> closedSet = new HashSet<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, int> gScore = new Dictionary<Vector2Int, int> { [startCell] = 0 };
        Dictionary<Vector2Int, int> fScore = new Dictionary<Vector2Int, int> { [startCell] = GetManhattanDistance(startCell, goalCell) };

        while (openSet.Count > 0)
        {
            Vector2Int current = GetBestOpenNode(openSet, fScore);
            if (current == goalCell)
            {
                BuildWorldPath(cameFrom, current, worldPath);
                return true;
            }

            openSet.Remove(current);
            closedSet.Add(current);

            for (int direction = 0; direction < 4; direction++)
            {
                if (!TryGetTraversableNeighbor(current, direction, out Vector2Int neighbor) ||
                    closedSet.Contains(neighbor))
                {
                    continue;
                }

                int currentG = GetScoreOrDefault(gScore, current, int.MaxValue / 4);
                int tentativeG = currentG + 1;

                if (!openSet.Contains(neighbor))
                {
                    openSet.Add(neighbor);
                }
                else if (tentativeG >= GetScoreOrDefault(gScore, neighbor, int.MaxValue / 4))
                {
                    continue;
                }

                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                fScore[neighbor] = tentativeG + GetManhattanDistance(neighbor, goalCell);
            }
        }

        return false;
    }

    public bool IsWithinPathDistanceInTiles(Vector2Int fromCell, Vector2Int toCell, int maxDistance)
    {
        if (grid == null || width <= 0 || height <= 0)
        {
            return false;
        }

        int distanceLimit = Mathf.Max(0, maxDistance);
        if (!IsCellInBounds(fromCell) || !IsCellInBounds(toCell))
        {
            return false;
        }

        if (fromCell == toCell)
        {
            return true;
        }

        if (distanceLimit == 0)
        {
            return false;
        }

        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        Dictionary<Vector2Int, int> visitedDistance = new Dictionary<Vector2Int, int>();

        frontier.Enqueue(fromCell);
        visitedDistance[fromCell] = 0;

        while (frontier.Count > 0)
        {
            Vector2Int current = frontier.Dequeue();
            int currentDistance = visitedDistance[current];
            if (currentDistance >= distanceLimit)
            {
                continue;
            }

            for (int direction = 0; direction < 4; direction++)
            {
                if (!TryGetTraversableNeighbor(current, direction, out Vector2Int neighbor))
                {
                    continue;
                }

                if (visitedDistance.ContainsKey(neighbor))
                {
                    continue;
                }

                int neighborDistance = currentDistance + 1;
                if (neighbor == toCell && neighborDistance <= distanceLimit)
                {
                    return true;
                }

                visitedDistance[neighbor] = neighborDistance;
                frontier.Enqueue(neighbor);
            }
        }

        return false;
    }

    public bool TryGetRandomAvailableCellWorldPosition(out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (availableCellsList == null || availableCellsList.Count == 0)
        {
            return false;
        }

        int randomIndex = Random.Range(0, availableCellsList.Count);
        worldPosition = CellToWorldPosition(availableCellsList[randomIndex]);
        return true;
    }

    void GenerateMaze()
    {
        const int maxGenerationAttempts = 128;

        for (int attempt = 1; attempt <= maxGenerationAttempts; attempt++)
        {
            InitializeGrid();
            GenerateMazeWithDepthFirstSearch();

            if (!HasFullyOpenCells())
            {
                return;
            }
        }

        Debug.LogWarning("[MazeGenerator] Falling back to snake maze generation to guarantee every tile keeps at least one wall.");
        InitializeGrid();
        GenerateSnakeMaze();
    }

    private void InitializeGrid()
    {
        grid = new Cell[width, height];
        stack.Clear();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Cell();
            }
        }
    }

    private void GenerateMazeWithDepthFirstSearch()
    {
        // Start maze generation from the top-left cell
        Vector2Int currentCell = new Vector2Int(0, 0);
        grid[currentCell.x, currentCell.y].visited = true;

        // Begin the recursive backtracking algorithm
        stack.Push(currentCell);

        while (stack.Count > 0)
        {
            currentCell = stack.Pop();
            List<Vector2Int> neighbors = GetUnvisitedNeighbors(currentCell);

            if (neighbors.Count > 0)
            {
                stack.Push(currentCell);

                // Choose a random neighbor
                Vector2Int chosenNeighbor = neighbors[UnityEngine.Random.Range(0, neighbors.Count)];

                // Remove the wall between current cell and chosen neighbor
                RemoveWall(currentCell, chosenNeighbor);

                // Mark the neighbor as visited and push it to the stack
                grid[chosenNeighbor.x, chosenNeighbor.y].visited = true;
                stack.Push(chosenNeighbor);
            }
        }
    }

    private void GenerateSnakeMaze()
    {
        Vector2Int? previousCell = null;

        for (int y = 0; y < height; y++)
        {
            if ((y & 1) == 0)
            {
                for (int x = 0; x < width; x++)
                {
                    ConnectSnakeCell(new Vector2Int(x, y), ref previousCell);
                }
            }
            else
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    ConnectSnakeCell(new Vector2Int(x, y), ref previousCell);
                }
            }
        }
    }

    private void ConnectSnakeCell(Vector2Int currentCell, ref Vector2Int? previousCell)
    {
        grid[currentCell.x, currentCell.y].visited = true;

        if (previousCell.HasValue)
        {
            RemoveWall(previousCell.Value, currentCell);
        }

        previousCell = currentCell;
    }
    
    List<Vector2Int> GetUnvisitedNeighbors(Vector2Int cell)
    {
        List<Vector2Int> neighbors = new List<Vector2Int>();

        // North neighbor
        if (cell.y + 1 < height && !grid[cell.x, cell.y + 1].visited)
        {
                   neighbors.Add(new Vector2Int(cell.x, cell.y + 1));
        }

        // East neighbor
        if (cell.x + 1 < width && !grid[cell.x + 1, cell.y].visited)
        {
            neighbors.Add(new Vector2Int(cell.x + 1, cell.y));
        }

        // South neighbor
        if (cell.y - 1 >= 0 && !grid[cell.x, cell.y - 1].visited)
        {
            neighbors.Add(new Vector2Int(cell.x, cell.y - 1));
        }

        // West neighbor
        if (cell.x - 1 >= 0 && !grid[cell.x - 1, cell.y].visited)
        {
            neighbors.Add(new Vector2Int(cell.x - 1, cell.y));
        }

        return neighbors;
    }

    void RemoveWall(Vector2Int current, Vector2Int neighbor)
    {
        if (current.x == neighbor.x)
        {
            if (current.y > neighbor.y)
            {
                // Neighbor is south
                grid[current.x, current.y].walls[2] = false; // Remove south wall
                grid[neighbor.x, neighbor.y].walls[0] = false; // Remove north wall
            }
            else
            {
                // Neighbor is north
                grid[current.x, current.y].walls[0] = false; // Remove north wall
                grid[neighbor.x, neighbor.y].walls[2] = false; // Remove south wall
            }
        }
        else if (current.y == neighbor.y)
        {
            if (current.x > neighbor.x)
            {
                // Neighbor is west
                grid[current.x, current.y].walls[3] = false; // Remove west wall
                grid[neighbor.x, neighbor.y].walls[1] = false; // Remove east wall
            }
            else
            {
                // Neighbor is east
                grid[current.x, current.y].walls[1] = false; // Remove east wall
                grid[neighbor.x, neighbor.y].walls[3] = false; // Remove west wall
            }
        }
    }

    void RemoveRandomWalls()
    {
        // Step 1: Collect all eligible internal walls
        List<(Vector2Int cell, int direction)> internalWalls = new List<(Vector2Int, int)>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                // Skip perimeter cells for outer walls
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    continue;
                }

                // East wall (1)
                if (x < width - 1 && grid[x, y].walls[1])
                {
                    internalWalls.Add((new Vector2Int(x, y), 1));
                }

                // South wall (2)
                if (y > 0 && grid[x, y].walls[2])
                {
                    internalWalls.Add((new Vector2Int(x, y), 2));
                }
            }
        }

        // Step 2: Shuffle the list to ensure randomness
        Shuffle(internalWalls);

        // Step 3: Calculate the number of walls to remove
        int totalInternalWalls = internalWalls.Count;
        int wallsToRemove = Mathf.RoundToInt(totalInternalWalls * wallRemovalPercentage);

        // Step 4: Remove the walls without creating fully open tiles
        int removedWallCount = 0;
        for (int i = 0; i < internalWalls.Count && removedWallCount < wallsToRemove; i++)
        {
            var (cell, direction) = internalWalls[i];
            if (TryRemoveWallIfTileKeepsAtLeastOneWall(cell, direction))
            {
                removedWallCount++;
            }
        }
    }

    private bool TryRemoveWallIfTileKeepsAtLeastOneWall(Vector2Int cell, int direction)
    {
        if (!IsCellInBounds(cell))
        {
            return false;
        }

        Vector2Int neighbor = GetNeighbor(cell, direction);
        if (!IsCellInBounds(neighbor))
        {
            return false;
        }

        Cell currentCell = grid[cell.x, cell.y];
        Cell neighborCell = grid[neighbor.x, neighbor.y];
        if (currentCell == null || neighborCell == null || currentCell.walls == null || neighborCell.walls == null)
        {
            return false;
        }

        int oppositeDirection = GetOppositeDirection(direction);
        if (direction < 0 ||
            direction >= currentCell.walls.Length ||
            oppositeDirection < 0 ||
            oppositeDirection >= neighborCell.walls.Length)
        {
            return false;
        }

        if (!currentCell.walls[direction] || !neighborCell.walls[oppositeDirection])
        {
            return false;
        }

        if (CountWalls(currentCell) <= 1 || CountWalls(neighborCell) <= 1)
        {
            return false;
        }

        if (WouldCreateOpenTwoByTwoArea(cell, direction))
        {
            return false;
        }

        currentCell.walls[direction] = false;
        neighborCell.walls[oppositeDirection] = false;
        return true;
    }

    Vector2Int GetNeighbor(Vector2Int cell, int direction)
    {
        switch (direction)
        {
            case 0: // North
                return new Vector2Int(cell.x, cell.y + 1);
            case 1: // East
                return new Vector2Int(cell.x + 1, cell.y);
            case 2: // South
                return new Vector2Int(cell.x, cell.y - 1);
            case 3: // West
                return new Vector2Int(cell.x - 1, cell.y);
            default:
                return cell;
        }
    }

    int GetOppositeDirection(int direction)
    {
        return (direction + 2) % 4;
    }

    private bool IsCellInBounds(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
    }

    private bool WouldCreateOpenTwoByTwoArea(Vector2Int cell, int direction)
    {
        Vector2Int neighbor = GetNeighbor(cell, direction);
        if (!IsCellInBounds(cell) || !IsCellInBounds(neighbor))
        {
            return false;
        }

        if (direction == 0 || direction == 2)
        {
            int blockOriginY = Mathf.Min(cell.y, neighbor.y);
            return IsFullyOpenTwoByTwoAfterWallRemoval(new Vector2Int(cell.x - 1, blockOriginY), cell, direction) ||
                   IsFullyOpenTwoByTwoAfterWallRemoval(new Vector2Int(cell.x, blockOriginY), cell, direction);
        }

        int blockOriginX = Mathf.Min(cell.x, neighbor.x);
        return IsFullyOpenTwoByTwoAfterWallRemoval(new Vector2Int(blockOriginX, cell.y - 1), cell, direction) ||
               IsFullyOpenTwoByTwoAfterWallRemoval(new Vector2Int(blockOriginX, cell.y), cell, direction);
    }

    private bool IsFullyOpenTwoByTwoAfterWallRemoval(Vector2Int blockOrigin, Vector2Int removedWallCell, int removedWallDirection)
    {
        if (blockOrigin.x < 0 || blockOrigin.y < 0 || blockOrigin.x >= width - 1 || blockOrigin.y >= height - 1)
        {
            return false;
        }

        Vector2Int bottomLeft = blockOrigin;
        Vector2Int bottomRight = new Vector2Int(blockOrigin.x + 1, blockOrigin.y);
        Vector2Int topLeft = new Vector2Int(blockOrigin.x, blockOrigin.y + 1);
        Vector2Int topRight = new Vector2Int(blockOrigin.x + 1, blockOrigin.y + 1);

        return HasPassageAfterWallRemoval(bottomLeft, 1, removedWallCell, removedWallDirection) &&
               HasPassageAfterWallRemoval(topLeft, 1, removedWallCell, removedWallDirection) &&
               HasPassageAfterWallRemoval(bottomLeft, 0, removedWallCell, removedWallDirection) &&
               HasPassageAfterWallRemoval(bottomRight, 0, removedWallCell, removedWallDirection);
    }

    private bool HasPassageAfterWallRemoval(Vector2Int fromCell, int direction, Vector2Int removedWallCell, int removedWallDirection)
    {
        Vector2Int toCell = GetNeighbor(fromCell, direction);
        if (!IsCellInBounds(fromCell) || !IsCellInBounds(toCell))
        {
            return false;
        }

        if (IsSameWall(fromCell, direction, removedWallCell, removedWallDirection))
        {
            return true;
        }

        Cell from = grid[fromCell.x, fromCell.y];
        Cell to = grid[toCell.x, toCell.y];
        if (from == null || to == null || from.walls == null || to.walls == null)
        {
            return false;
        }

        int oppositeDirection = GetOppositeDirection(direction);
        if (direction < 0 ||
            direction >= from.walls.Length ||
            oppositeDirection < 0 ||
            oppositeDirection >= to.walls.Length)
        {
            return false;
        }

        return !from.walls[direction] && !to.walls[oppositeDirection];
    }

    private bool IsSameWall(Vector2Int cellA, int directionA, Vector2Int cellB, int directionB)
    {
        if (cellA == cellB && directionA == directionB)
        {
            return true;
        }

        Vector2Int neighborA = GetNeighbor(cellA, directionA);
        Vector2Int neighborB = GetNeighbor(cellB, directionB);
        return cellA == neighborB &&
               neighborA == cellB &&
               directionA == GetOppositeDirection(directionB);
    }

    private bool HasFullyOpenCells()
    {
        if (grid == null)
        {
            return false;
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (CountWalls(grid[x, y]) == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountWalls(Cell cell)
    {
        if (cell == null || cell.walls == null)
        {
            return 0;
        }

        int wallCount = 0;
        int wallCountLength = Mathf.Min(4, cell.walls.Length);
        for (int i = 0; i < wallCountLength; i++)
        {
            if (cell.walls[i])
            {
                wallCount++;
            }
        }

        return wallCount;
    }

    private bool TryGetTraversableNeighbor(Vector2Int fromCell, int direction, out Vector2Int neighbor)
    {
        neighbor = GetNeighbor(fromCell, direction);
        if (!IsCellInBounds(fromCell) || !IsCellInBounds(neighbor))
        {
            return false;
        }

        Cell from = grid[fromCell.x, fromCell.y];
        Cell to = grid[neighbor.x, neighbor.y];
        if (from == null || to == null || from.walls == null || to.walls == null || from.walls.Length < 4 || to.walls.Length < 4)
        {
            return false;
        }

        if (from.walls[direction])
        {
            return false;
        }

        int oppositeDirection = GetOppositeDirection(direction);
        if (to.walls[oppositeDirection])
        {
            return false;
        }

        return true;
    }

    private static int GetManhattanDistance(Vector2Int from, Vector2Int to)
    {
        return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
    }

    private static int GetScoreOrDefault(Dictionary<Vector2Int, int> scores, Vector2Int key, int fallback)
    {
        return scores.TryGetValue(key, out int value) ? value : fallback;
    }

    private static Vector2Int GetBestOpenNode(List<Vector2Int> openSet, Dictionary<Vector2Int, int> fScore)
    {
        Vector2Int bestNode = openSet[0];
        int bestScore = GetScoreOrDefault(fScore, bestNode, int.MaxValue / 4);

        for (int i = 1; i < openSet.Count; i++)
        {
            Vector2Int node = openSet[i];
            int nodeScore = GetScoreOrDefault(fScore, node, int.MaxValue / 4);
            if (nodeScore < bestScore)
            {
                bestScore = nodeScore;
                bestNode = node;
            }
        }

        return bestNode;
    }

    private void BuildWorldPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int currentNode, List<Vector2> worldPath)
    {
        worldPath.Clear();
        worldPath.Add(CellToWorldPosition(currentNode));

        while (cameFrom.TryGetValue(currentNode, out Vector2Int previousNode))
        {
            currentNode = previousNode;
            worldPath.Add(CellToWorldPosition(currentNode));
        }

        worldPath.Reverse();
    }


    void DrawMaze()
    {
        if (mazeParent == null)
        {
            GameObject mazeParentObj = new GameObject("MazeParent");
            mazeParent = mazeParentObj.transform;
        }

        if (mazeParent.parent != null)
        {
            mazeParent.SetParent(null, false);
        }

        mazeParent.position = Vector3.zero;
        mazeParent.rotation = Quaternion.identity;
        mazeParent.localScale = Vector3.one;

        // Clear existing maze objects
        foreach (Transform child in mazeParent)
        {
            Destroy(child.gameObject);
        }

        // Calculate offsets to center the maze
        float mazeWidth = width * cellSize;
        float mazeHeight = height * cellSize;
        float offsetX = -mazeWidth / 2 + cellSize / 2;
        float offsetY = -mazeHeight / 2 + cellSize / 2;

        InstantiateCombinedFloor(mazeWidth, mazeHeight);

        DrawMergedHorizontalWalls(offsetX, offsetY);
        DrawMergedVerticalWalls(offsetX, offsetY);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                InstantiateCornerPrefabs(x, y, grid[x, y], offsetX, offsetY);
            }
        }

        AdjustCamera();
    }

    private void InstantiateCombinedFloor(float mazeWidth, float mazeHeight)
    {
        if (floorPrefab == null)
        {
            return;
        }

        GameObject floor = Instantiate(floorPrefab, Vector3.zero, Quaternion.identity, mazeParent);
        floor.transform.localPosition = Vector3.zero;
        floor.transform.localRotation = Quaternion.identity;
        floor.transform.localScale = new Vector3(
            Mathf.Max(cellSize, mazeWidth),
            Mathf.Max(cellSize, mazeHeight),
            1f);
    }

    private void DrawMergedHorizontalWalls(float offsetX, float offsetY)
    {
        for (int boundaryY = 0; boundaryY <= height; boundaryY++)
        {
            int runStartX = -1;

            for (int x = 0; x <= width; x++)
            {
                bool hasWall = x < width && HasHorizontalWallAt(x, boundaryY);
                if (hasWall)
                {
                    if (runStartX < 0)
                    {
                        runStartX = x;
                    }

                    continue;
                }

                if (runStartX >= 0)
                {
                    int runLength = x - runStartX;
                    InstantiateHorizontalWallRun(runStartX, boundaryY, runLength, offsetX, offsetY);
                    runStartX = -1;
                }
            }
        }
    }

    private void DrawMergedVerticalWalls(float offsetX, float offsetY)
    {
        for (int boundaryX = 0; boundaryX <= width; boundaryX++)
        {
            int runStartY = -1;

            for (int y = 0; y <= height; y++)
            {
                bool hasWall = y < height && HasVerticalWallAt(boundaryX, y);
                if (hasWall)
                {
                    if (runStartY < 0)
                    {
                        runStartY = y;
                    }

                    continue;
                }

                if (runStartY >= 0)
                {
                    int runLength = y - runStartY;
                    InstantiateVerticalWallRun(boundaryX, runStartY, runLength, offsetX, offsetY);
                    runStartY = -1;
                }
            }
        }
    }

    private bool HasHorizontalWallAt(int cellX, int boundaryY)
    {
        if (grid == null || cellX < 0 || cellX >= width || boundaryY < 0 || boundaryY > height)
        {
            return false;
        }

        if (boundaryY == 0)
        {
            return grid[cellX, 0].walls[2];
        }

        if (boundaryY == height)
        {
            return grid[cellX, height - 1].walls[0];
        }

        return grid[cellX, boundaryY - 1].walls[0];
    }

    private bool HasVerticalWallAt(int boundaryX, int cellY)
    {
        if (grid == null || boundaryX < 0 || boundaryX > width || cellY < 0 || cellY >= height)
        {
            return false;
        }

        if (boundaryX == 0)
        {
            return grid[0, cellY].walls[3];
        }

        if (boundaryX == width)
        {
            return grid[width - 1, cellY].walls[1];
        }

        return grid[boundaryX - 1, cellY].walls[1];
    }

    private void InstantiateHorizontalWallRun(int startCellX, int boundaryY, int runLength, float offsetX, float offsetY)
    {
        if (wallPrefab == null || runLength <= 0)
        {
            return;
        }

        float centerX = offsetX + (startCellX + runLength * 0.5f - 0.5f) * cellSize;
        float y = offsetY - cellSize * 0.5f + boundaryY * cellSize;
        GameObject wall = Instantiate(wallPrefab, new Vector3(centerX, y, 0f), Quaternion.Euler(0f, 0f, 90f), mazeParent);
        ApplyWallRunScale(wall.transform, runLength);
    }

    private void InstantiateVerticalWallRun(int boundaryX, int startCellY, int runLength, float offsetX, float offsetY)
    {
        if (wallPrefab == null || runLength <= 0)
        {
            return;
        }

        float x = offsetX - cellSize * 0.5f + boundaryX * cellSize;
        float centerY = offsetY + (startCellY + runLength * 0.5f - 0.5f) * cellSize;
        GameObject wall = Instantiate(wallPrefab, new Vector3(x, centerY, 0f), Quaternion.identity, mazeParent);
        ApplyWallRunScale(wall.transform, runLength);
    }

    private void ApplyWallRunScale(Transform wallTransform, int runLength)
    {
        if (wallTransform == null)
        {
            return;
        }

        Vector3 baseScale = wallPrefab.transform.localScale;
        wallTransform.localScale = new Vector3(
            baseScale.x,
            baseScale.y * Mathf.Max(1, runLength) * cellSize,
            baseScale.z);
    }

   // Serialize maze data into a format that can be sent to clients
    private int[] SerializeMazeData()
    {
        // Total elements: 2 for width and height + 4 for each cell's walls
        int[] serializedData = new int[2 + width * height * 4];
        
        // First two elements are width and height
        serializedData[0] = width;
        serializedData[1] = height;
        
        int index = 2; // Start after width and height
        
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Cell cell = grid[x, y];
                serializedData[index++] = cell.walls[0] ? 1 : 0; // North wall
                serializedData[index++] = cell.walls[1] ? 1 : 0; // East wall
                serializedData[index++] = cell.walls[2] ? 1 : 0; // South wall
                serializedData[index++] = cell.walls[3] ? 1 : 0; // West wall
            }
        }

        return serializedData;
    }


    // Deserialize maze data sent from the server
    private void DeserializeMazeData(int[] data)
    {
        if (data.Length < 2)
        {
            Debug.LogError("[MazeGenerator] Serialized data is too short to contain width and height.");
            return;
        }

        // Extract width and height
        width = data[0];
        height = data[1];

        // Initialize the grid with the new dimensions
        grid = new Cell[width, height];
        int index = 2; // Start after width and height

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (index + 4 > data.Length)
                {
                    Debug.LogError("[MazeGenerator] Serialized data is too short to contain all wall information.");
                    return;
                }

                grid[x, y] = new Cell
                {
                    walls = new bool[]
                    {
                        data[index++] == 1, // North wall
                        data[index++] == 1, // East wall
                        data[index++] == 1, // South wall
                        data[index++] == 1  // West wall
                    }
                };
            }
        }

        // After deserialization, draw the maze with the new dimensions
        DrawMaze();
    }

    void InstantiateCornerPrefabs(int x, int y, Cell cell, float offsetX, float offsetY)
    {
        // Define the four corners and their adjacent walls
        // Each tuple contains:
        // (Corner Position Offset X, Corner Position Offset Y, Adjacent Wall Indices)

        var corners = new List<(float, float, int, int)>
        {
            // Top-Left Corner
            (-cellSize / 2, cellSize / 2, 0, 3), // North and West walls

            // Top-Right Corner
            (cellSize / 2, cellSize / 2, 0, 1),  // North and East walls

            // Bottom-Left Corner
            (-cellSize / 2, -cellSize / 2, 2, 3), // South and West walls

            // Bottom-Right Corner
            (cellSize / 2, -cellSize / 2, 2, 1)   // South and East walls
        };

        foreach (var corner in corners)
        {
            float cornerOffsetX = corner.Item1;
            float cornerOffsetY = corner.Item2;
            int wallIndex1 = corner.Item3;
            int wallIndex2 = corner.Item4;

            // Check if both adjacent walls are present
            if (cell.walls[wallIndex1] && cell.walls[wallIndex2])
            {
                Vector3 cornerPosition = new Vector3(
                    x * cellSize + offsetX + cornerOffsetX,
                    y * cellSize + offsetY + cornerOffsetY,
                    0
                );
                Instantiate(cornerPrefab, cornerPosition, Quaternion.identity, mazeParent);
            }
        }
    }

    void AdjustCamera()
    {
        float mazeWidth = width * cellSize;
        float mazeHeight = height * cellSize; 
        float bottomPadding = mazeHeight * 0.1f;
        float mazeHeightWithPadding = mazeHeight + bottomPadding * 2f * 1.4f; // 1.2f padding at the top
        
        float centerX = (width - 1) * cellSize / 2f;
        float centerY = (height - 1) * cellSize / 2f;
        Vector3 mazeCenter = new Vector3(centerX, centerY, 0f);

        Camera cam = Camera.main;

        float aspectRatio = cam.aspect;
        float verticalSize = mazeHeightWithPadding / 2f;
        float horizontalSize = mazeWidth / (2f * aspectRatio);

        cam.orthographicSize = Mathf.Max(verticalSize, horizontalSize);

        cam.transform.position = new Vector3(0, 0 - bottomPadding, -10f);
    }
}
