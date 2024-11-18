using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Cell
{
    public bool visited = false;
    public bool[] walls = { true, true, true, true }; // North, East, South, West
}

public class MazeGenerator : MonoBehaviour
{
    public int width = 15;
    public int height = 15;
    public float cellSize = 1.0f;

    public GameObject floorPrefab;
    public GameObject wallPrefab;

    private Cell[,] grid;
    private Stack<Vector2Int> stack = new Stack<Vector2Int>();

    void Start()
    {
        GenerateMaze();
        DrawMaze();
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

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector3 cellPosition = new Vector3(x * cellSize, y * cellSize, 0);

                // Instantiate floor
                Instantiate(floorPrefab, cellPosition, Quaternion.identity, transform);

                // Instantiate walls based on the cell's walls
                Cell cell = grid[x, y];

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
}
