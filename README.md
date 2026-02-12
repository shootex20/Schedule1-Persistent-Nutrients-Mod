# Schedule1-Persistent-Nutrients-Mod
<img width="1024" height="1536" alt="PersistentNutrients" src="https://github.com/user-attachments/assets/a03afad2-b108-4a76-890a-3279698332bc" />

## Required DLL's (Probably an easier way to do this...)
<img width="677" height="263" alt="image" src="https://github.com/user-attachments/assets/822496e0-ac3f-4a84-89d4-ba33b07c0cd9" />

PersistentNutrients Mod makes fertilizers persist with soil across multiple plant cycles in Schedule 1.
Key Features:
•	Saves fertilizer effects (yield & quality bonuses) when harvesting a plant if soil still has uses remaining
•	Automatically restores fertilizer bonuses when planting a new seed in previously fertilized soil
•	Prevents re-fertilizing already fertilized soil (both manually and by botanist employees)
•	Handles Speed Grow separately - applies 50% instant growth on restoration since it's a one-time effect
•	Per-save-slot persistence - fertilizer data is saved/loaded with each game save using JSON files
•	Auto-cleanup - removes fertilizer data when soil is depleted
How It Works:
Instead of losing fertilizer bonuses after each harvest, the mod captures the yield/quality multipliers and reapplies them to the next plant in the same pot, effectively making fertilizers last for the entire lifetime of the soil rather than just one plant cycle.

