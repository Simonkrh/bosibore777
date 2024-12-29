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
    public int width = 15;
    public int height = 15;
    public float cellSize = 1.0f;

    public GameObject floorPrefab;
    public GameObject wallPrefab;
    public GameObject cornerPrefab;
    public Transform mazeParent;

    private GameManager gameManager;
    private Cell[,] grid;
    private Stack<Vector2Int> stack = new Stack<Vector2Int>();
    private List<Vector2Int> availableCellsList;

    public override void OnNetworkSpawn()
    {
        // Only the dedicated server or host will generate the maze and sync to clients
        if (IsServer)
        {
            Debug.Log("[Server] Generating initial maze...");
            RegenerateMaze();

            // Register callback for new client connections
            if (CustomNetworkManager.Singleton != null)
                CustomNetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
        else
        {
            Debug.Log("[Client] Waiting for maze data from server...");
        }
    }

    public void RegenerateMaze()
    {
        Debug.Log("[MazeGenerator] Regenerating maze...");
        GenerateMaze();
        DrawMaze();
        InitializeAvailableCells();
        Shuffle(availableCellsList);
        NotifyAvailableCellsReady();

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
    
    private void Shuffle(List<Vector2Int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            Vector2Int temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    public void NotifyAvailableCellsReady()
    {
        // Debug.Log($"[MazeGenerator] Notifying available cells ready: {availableCellsList.Count}");
        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.SetAvailableCells(new List<Vector2Int>(availableCellsList));
        }
        else
        {
            Debug.LogError("[MazeGenerator] GameManager not found. Cannot set available cells.");
        }
    }

    private void OnDestroy()
    {
        // Unregister the callback when this object is destroyed
        if (CustomNetworkManager.Singleton != null)
        {
            CustomNetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Server] Client {clientId} connected. Sending maze data...");

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
        if (!IsClient) return;
        Debug.Log($"[Client {CustomNetworkManager.Singleton.LocalClientId}] Received maze data. Deserializing...");
        DeserializeMazeData(serializedData);

        DrawMaze();
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

    void GenerateMaze()
    {
        grid = new Cell[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                grid[x, y] = new Cell();
            }
        }

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

    void DrawMaze()
{
    if (mazeParent == null)
    {
        GameObject mazeParentObj = new GameObject("MazeParent");
        mazeParent = mazeParentObj.transform;
    }

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

    for (int x = 0; x < width; x++)
    {
        for (int y = 0; y < height; y++)
        {
            // Adjust cell position to center the maze
            Vector3 cellPosition = new Vector3(x * cellSize + offsetX, y * cellSize + offsetY, 0);

            // Instantiate floor
            Instantiate(floorPrefab, cellPosition, Quaternion.identity, mazeParent);

            // Instantiate walls based on the cell's walls
            Cell cell = grid[x, y];
            
            // Instantiate corner blocks
            Vector3 cornerPosition = new Vector3(
                (x * cellSize) + offsetX - (cellSize / 2),
                (y * cellSize) + offsetY - (cellSize / 2),
                0
            );
            Instantiate(cornerPrefab, cornerPosition, Quaternion.identity, mazeParent);

            // North wall
            if (cell.walls[0])
            {
                Vector3 position = cellPosition + new Vector3(0, cellSize / 2, 0);
                Instantiate(wallPrefab, position, Quaternion.Euler(0, 0, 90), mazeParent);
            }

            // East wall
            if (cell.walls[1])
            {
                Vector3 position = cellPosition + new Vector3(cellSize / 2, 0, 0);
                Instantiate(wallPrefab, position, Quaternion.identity, mazeParent);
            }

            // South wall
            if (cell.walls[2])
            {
                Vector3 position = cellPosition + new Vector3(0, -cellSize / 2, 0);
                Instantiate(wallPrefab, position, Quaternion.Euler(0, 0, 90), mazeParent);
            }

            // West wall
            if (cell.walls[3])
            {
                Vector3 position = cellPosition + new Vector3(-cellSize / 2, 0, 0);
                Instantiate(wallPrefab, position, Quaternion.identity, mazeParent);
            }
        }
    }

    AdjustCamera();
}

   // Serialize maze data into a format that can be sent to clients
    private int[] SerializeMazeData()
    {
        int[] serializedData = new int[width * height * 4];
        int index = 0;

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
        grid = new Cell[width, height];
        int index = 0;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
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
