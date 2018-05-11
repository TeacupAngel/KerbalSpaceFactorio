# KerbalSpaceFactorio

# This project is superseeded by https://github.com/Rahjital/XenoIndustry

To download the latest release, visit [github releases](https://github.com/Danielv123/KerbalSpaceFactorio/releases)

* Build rocketparts in factorio

* Get funds in KSP

* Launch missions in KSP

* Return science packs to factorio

* Profit

All credit for making the KSP side of this mod goes to /u/dragon-storyteller

## Setup

1. Install [Clusterio](https://github.com/Danielv123/factorioClusterio)

2. Install KSP and all that

3. Grab the latest release off of [github releases](https://github.com/Danielv123/KerbalSpaceFactorio/releases)

4. Install the mod in KSP

5. Change the config.json located inside the mod. You must set the masterIP and port to your clusterio masterserver, see point 1.

## Gameplay

Build rocketparts in factorio and place them in put-chests to send them to the master server.

Open KSP and build a rocket. There should be a white gear you can press to access the mod menu. The cost of you ship is determined by funds, just like carreer mode. What would normall be 100 funds is now 1 low density structure.

Use the get-chest to import space science packs in factorio.

## A couple of things that may be good to know:

- It's likely unstable. There were a few bugs I knew about that I may or may not have fixed, I honestly don't remember...

- There's no safeties, the mod is always active as long as it is installed. It'd be a good idea to start a new science game for this (sandbox is not tested or recommended; masochists can try career too)

- There's a json config file in the mod directory you can use to choose master IP and port, tweak some gameplay values (cost of rockets and science points needed per Factorio science pack), or enable debug mode to cheat in some resources to the Clusterio server

- You can only launch from the VAB and SPH as I haven't figured out the appropriate Reflection detour for launching from the pad and runway. I did a last minute quick fix, but it would still be better idea not to try at all

- The mod doesn't tell you if it fails to connect to the Clusterio server, you can only tell if you have some low density structures in the Clusterio storage and the mod doesn't see them
