using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ThinWallMazeGenerator : MonoBehaviour
{
    public int width = 15; // Number of cells horizontally
    public int height = 15; // Number of cells vertically
    public GameObject thinWallPrefab; // Prefab for thin walls
    public GameObject floorPrefab; // Prefab for the floor
    public float cellSize = 1.0f; // Size of each cell

    private int[,] maze; // The maze grid (1 = wall, 0 = path)

    void Start()
    {
        GenerateMaze();
        DrawMaze();
    }

    void GenerateMaze()
    {
        // Initialize maze grid
        maze = new int[width * 2 + 1, height * 2 + 1];

        // Fill the maze with walls
        for (int x = 0; x < maze.GetLength(0); x++)
        {
            for (int y = 0; y < maze.GetLength(1); y++)
            {
                maze[x, y] = 1; // 1 = Wall
            }
        }

        // Carve the paths
        CarvePath(1, 1);

        // Add additional openings if needed
        AddOpenings();
    }

    void CarvePath(int x, int y)
    {
        maze[x, y] = 0; // Mark the current cell as a path

        // Randomized directions for carving
        List<Vector2Int> directions = new List<Vector2Int> {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };
        Shuffle(directions);

        foreach (var direction in directions)
        {
            int nx = x + direction.x * 2; // Neighbor cell
            int ny = y + direction.y * 2;

            if (nx > 0 && nx < maze.GetLength(0) - 1 && ny > 0 && ny < maze.GetLength(1) - 1 && maze[nx, ny] == 1)
            {
                // Carve the wall between the current cell and the neighbor
                maze[x + direction.x, y + direction.y] = 0;
                // Recursively carve the neighbor
                CarvePath(nx, ny);
            }
        }
    }

    void AddOpenings()
    {
        // Add random openings around the edges of the maze
        for (int i = 0; i < 2; i++)
        {
            int x = Random.Range(1, width * 2 - 1);
            int y = (i == 0) ? 0 : height * 2; // Top or bottom edge
            maze[x, y] = 0;
        }
    }

   void DrawMaze()
{
    // Clear existing children
    foreach (Transform child in transform)
    {
        Destroy(child.gameObject);
    }

    // Draw the maze
    for (int x = 0; x < maze.GetLength(0); x++)
    {
        for (int y = 0; y < maze.GetLength(1); y++)
        {
            // Determine the position in the world
            float posX = (x / 2f) * cellSize;
            float posY = (y / 2f) * cellSize;
            Vector3 position = new Vector3(posX, posY, 0);

            if (maze[x, y] == 1)
            {
                if (x % 2 == 1 && y % 2 == 1)
                {
                    // This is a cell center; do nothing.
                }
                else if (x % 2 == 0 && y % 2 == 0)
                {
                    // This is a corner; you can skip or place a small block if desired.
                }
                else if (x % 2 == 0 && y % 2 == 1)
                {
                    // Vertical wall
                    Instantiate(thinWallPrefab, position, Quaternion.identity, transform);
                }
                else if (x % 2 == 1 && y % 2 == 0)
                {
                    // Horizontal wall (rotate 90 degrees)
                    Instantiate(thinWallPrefab, position, Quaternion.Euler(0, 0, 90), transform);
                }
            }
            else
            {
                if (x % 2 == 1 && y % 2 == 1)
                {
                    // This is a path cell; place a floor tile.
                    Instantiate(floorPrefab, position, Quaternion.identity, transform);
                }
                // Open space (no wall); do nothing.
            }
        }
    }
}


    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);
            T temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }
}
