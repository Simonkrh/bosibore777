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

    public float paddingTop = 1.0f;
    public float paddingBottom = 1.0f;
    public float paddingLeft = 1.0f;
    public float paddingRight = 1.0f;

    private Cell[,] grid;
    private Stack<Vector2Int> stack = new Stack<Vector2Int>();

    public override void OnNetworkSpawn()
    {
        Debug.Log($"OnNetworkSpawn called. IsHost: {IsHost}");
        if (IsHost)
        {
            GenerateMaze();
            DrawMaze();

            // Send maze data to clients
            SyncMazeDataToClientsServerRpc(SerializeMazeData());

            base.OnNetworkSpawn();
        }
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
                Vector2Int chosenNeighbor = neighbors[Random.Range(0, neighbors.Count)];

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
        // Clear existing maze objects
        foreach (Transform child in transform)
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
                Instantiate(floorPrefab, cellPosition, Quaternion.identity, transform);

                // Instantiate walls based on the cell's walls
                Cell cell = grid[x, y];
                
                // Instantiate corner blocks
                Vector3 cornerPosition = new Vector3(
                    (x * cellSize) + offsetX - (cellSize / 2),
                    (y * cellSize) + offsetY - (cellSize / 2),
                    0
                );
                Instantiate(cornerPrefab, cornerPosition, Quaternion.identity, transform);

                // North wall
                if (cell.walls[0])
                {
                    Vector3 position = cellPosition + new Vector3(0, cellSize / 2, 0);
                    Instantiate(wallPrefab, position, Quaternion.Euler(0, 0, 90), transform);
                }

                // East wall
                if (cell.walls[1])
                {
                    Vector3 position = cellPosition + new Vector3(cellSize / 2, 0, 0);
                    Instantiate(wallPrefab, position, Quaternion.identity, transform);
                }

                // South wall
                if (cell.walls[2])
                {
                    Vector3 position = cellPosition + new Vector3(0, -cellSize / 2, 0);
                    Instantiate(wallPrefab, position, Quaternion.Euler(0, 0, 90), transform);
                }

                // West wall
                if (cell.walls[3])
                {
                    Vector3 position = cellPosition + new Vector3(-cellSize / 2, 0, 0);
                    Instantiate(wallPrefab, position, Quaternion.identity, transform);
                }
            }
        }
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

    // Deserialize maze data sent from the host
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

    // ServerRpc to sync maze data with clients
    [ServerRpc(RequireOwnership = false)]
    private void SyncMazeDataToClientsServerRpc(int[] serializedData)
    {
        SyncMazeDataToClientsClientRpc(serializedData);
    }

    // ClientRpc to apply maze data on clients
    [ClientRpc]
    private void SyncMazeDataToClientsClientRpc(int[] serializedData)
    {
        DeserializeMazeData(serializedData);
        DrawMaze(); // Draw the maze on clients
    }

    void AdjustCamera()
    {
        // Calculate maze dimensions in world units
        float mazeWidth = width * cellSize;
        float mazeHeight = height * cellSize;

        // Get the main camera
        Camera mainCamera = Camera.main;

        if (mainCamera != null)
        {
            if (mainCamera.orthographic)
            {
                // Calculate total width and height including padding
                float totalWidth = mazeWidth + paddingLeft + paddingRight;
                float totalHeight = mazeHeight + paddingTop + paddingBottom;

                // Determine the aspect ratio
                float screenAspect = (float)Screen.width / (float)Screen.height;
                float mazeAspect = totalWidth / totalHeight;

                // Adjust orthographic size to fit the maze with padding
                if (screenAspect >= mazeAspect)
                {
                    // Screen is wider than the maze with padding
                    mainCamera.orthographicSize = totalHeight / 2;
                }
                else
                {
                    // Screen is taller than the maze with padding
                    mainCamera.orthographicSize = (totalWidth / 2) / screenAspect;
                }

                // Position the camera to center the maze with padding
                float cameraX = (paddingLeft - paddingRight) / 2;
                float cameraY = (paddingTop - paddingBottom) / 2;

                mainCamera.transform.position = new Vector3(cameraX, cameraY, -10); // Adjust Z as needed
            }
            else
            {
                Debug.LogWarning("Camera is not orthographic. Adjustments may not work as intended.");
            }
        }
        else
        {
            Debug.LogError("Main camera not found. Please tag your camera as 'MainCamera'.");
        }
    }
}
