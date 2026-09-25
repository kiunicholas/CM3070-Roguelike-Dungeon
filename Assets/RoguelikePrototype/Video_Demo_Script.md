# Suggested 3-5 Minute Demo Video Script

## 0:00-0:30 Introduction
This prototype demonstrates the main technical feature of my final year project: procedural dungeon generation for a 2D roguelike dungeon exploration game. The project is implemented in Unity using C#.

## 0:30-1:10 Motivation
The aim is to reduce reliance on manually designed levels and improve replayability. Each time the dungeon is generated, the room layout, enemy placement, loot placement, and exit position can change.

## 1:10-2:20 Prototype Demonstration
Here is the generated dungeon. The player starts in the start room and can move using WASD or the arrow keys. The dungeon contains connected rooms, corridors, enemies, loot, and an exit. The system uses a grid-based layout and stores rooms as graph nodes.

## 2:20-3:00 Regeneration
Pressing R regenerates the dungeon. This shows the replayability aspect of the system because a new dungeon layout can be created at runtime.

## 3:00-4:10 Evaluation
The prototype also includes evaluation support. Pressing T runs 100 dungeon generation tests. The Console prints the valid dungeon rate, generation time, room count, exit distance, and branch count. These metrics help evaluate whether the generator creates playable and varied dungeons.

## 4:10-5:00 Limitations and Improvements
The prototype proves that the main feature is feasible, but it is not the final game. Future improvements include better enemy AI, more room types, improved combat, stronger loot balancing, and more detailed player testing.
