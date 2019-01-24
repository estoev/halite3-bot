using Halite3.hlt;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MyBot;

// TODO: At final return crash empty ships into enemy

namespace Halite3
{
    public enum ShipStatus
    {
        Exploring,
        Gathering,
        Returning,
        Building,
    }

    public class DropoffCellInfo
    {
        public int Id = -1;
        public int Distance = -1;
        public double Fuel;
    }

    public class ShipCellInfo
    {
        public int Distance = -1;
        public double Fuel;
    }

    public class CellInfo
    {
        public DropoffCellInfo Dropoff = new DropoffCellInfo();
        //public Dictionary<int, ShipCellInfo> Ships = new Dictionary<int, ShipCellInfo>();
    }

    public class MyBot
    {
        static int minHaliteToReturn;
        static int currentHalite;
        static int minScoreForDropoff;
        static int shipsToFirstDropoff;
        static int shipsToNextDropoff;
        static int playUntilTurn = 1000;
        static bool limitToOwnArea;
        static int limitAreaFromX, limitAreaToX, limitAreaFromY, limitAreaToY;
        static Game game;
        static Player me;
        static Player[] enemyPlayers;
        static GameMap gameMap;
        static CellInfo[][] scoreMap;
        static Dictionary<int, Dictionary<int, ShipCellInfo>> shipFuelInfo = new Dictionary<int, Dictionary<int, ShipCellInfo>>();
        static bool[,] positionMap;
        static Dictionary<int, bool> boostMap = new Dictionary<int, bool>();
        static readonly Dictionary<int, Position> ShipPositions = new Dictionary<int, Position>();
        static readonly Dictionary<int, ShipStatus> ShipStatuses = new Dictionary<int, ShipStatus>();
        static Random rng;
        static bool finalReturn;
        static readonly List<Position> HarvestTargets = new List<Position>();
        static readonly Dictionary<int, Position> LastHarvestTargets = new Dictionary<int, Position>();
        static readonly List<int> ResumeReturning = new List<int>();
        static readonly int DropoffRewardDistance = 15;
        static readonly Dictionary<string, string> pArgs = new Dictionary<string, string>();
        static readonly List<int> possibleConflicts = new List<int>();
        static readonly List<int> priorityQueue = new List<int>();
        static Position buildDropoffPosition = null;
        static HaliteStats haliteStats;
        static Dictionary<int, int> dropoffHaliteAccess = new Dictionary<int, int>();
        static Dictionary<int, int> relocateShips = new Dictionary<int, int>();
        static bool relocateShipsNextTurn = false;

        public static void Main(string[] args)
        {
            var configFile = Path.Combine(AppContext.BaseDirectory, "config.txt");
            string argStr = " ";
            if (File.Exists(configFile))
            {
                var lines = File.ReadAllLines(configFile);
                foreach (var line in lines)
                {
                    var keyVal = line.Split('=');
                    pArgs.Add(keyVal[0].TrimStart('_'), keyVal[1]);
                    if (!keyVal[0].StartsWith('_'))
                        argStr += keyVal[1] + "_";
                }
            }

            int rngSeed;
            rngSeed = args.Length > 1 ? int.Parse(args[1]) : DateTime.Now.Millisecond;
            rng = new Random(rngSeed);

            game = new Game();
            gameMap = game.gameMap;
            
            scoreMap = new CellInfo[gameMap.height][];
            for (var y = 0; y < scoreMap.Length; y++)
            {
                scoreMap[y] = new CellInfo[gameMap.width];
                for (int x = 0; x < gameMap.width; x++)
                {
                    scoreMap[y][x] = new CellInfo();
                }
            }

            minHaliteToReturn = (int) (Constants.MAX_HALITE * 0.95);
            minScoreForDropoff = 7000;
            shipsToFirstDropoff = 12;
            shipsToNextDropoff = 15;
            var maxDropoffs = 0;

            var totalHalite = 0;
            for (int x = 0; x < gameMap.width; x++)
            {
                for (int y = 0; y < gameMap.height; y++)
                {
                    totalHalite += gameMap.At(new Position(x, y)).halite;
                }
            }

            currentHalite = totalHalite;

            if (game.players.Count == 2)
            {
                if (gameMap.width == 32)
                {
                    maxDropoffs = 1;
                    shipsToFirstDropoff = 10;
                }

                if (gameMap.width == 40)
                {
                    maxDropoffs = 2;
                }

                if (gameMap.width == 48)
                {
                    maxDropoffs = 3;
                }

                if (gameMap.width == 56)
                {
                    maxDropoffs = 5;
                }

                if (gameMap.width == 64)
                {
                    maxDropoffs = 8;
                }
            }
            else
            {
                if (gameMap.width == 32)
                {
                    maxDropoffs = 0;
                }

                if (gameMap.width == 40)
                {
                    maxDropoffs = 2;
                    shipsToFirstDropoff = 12;
                    shipsToNextDropoff = 7;
                }

                if (gameMap.width == 48)
                {
                    maxDropoffs = 2;
                }

                if (gameMap.width == 56)
                {
                    maxDropoffs = 3;
                }

                if (gameMap.width == 64)
                {
                    maxDropoffs = 4;
                }
            }

            if (pArgs.ContainsKey("MAX_DROPOFFS"))
                maxDropoffs = int.Parse(pArgs["MAX_DROPOFFS"]);
            if (pArgs.ContainsKey("MIN_SCORE_FOR_DROPOFF"))
                minScoreForDropoff = int.Parse(pArgs["MIN_SCORE_FOR_DROPOFF"]);
            if (pArgs.ContainsKey("SHIPS_TO_FIRST_DROPOFF"))
                shipsToFirstDropoff = int.Parse(pArgs["SHIPS_TO_FIRST_DROPOFF"]);
            if (pArgs.ContainsKey("SHIPS_TO_NEXT_DROPOFF"))
                shipsToNextDropoff = int.Parse(pArgs["SHIPS_TO_NEXT_DROPOFF"]);

            if (pArgs.ContainsKey("PLAY_UNTIL_TURN"))
                playUntilTurn = int.Parse(pArgs["PLAY_UNTIL_TURN"]);

            if (pArgs.ContainsKey("LIMIT_TO_OWN_AREA"))
                limitToOwnArea = bool.Parse(pArgs["LIMIT_TO_OWN_AREA"]);
            if (limitToOwnArea)
            {
                limitAreaFromX = game.me.shipyard.position.x < gameMap.width / 2 ? 0 : gameMap.width / 2;
                limitAreaToX = game.me.shipyard.position.x < gameMap.width / 2 ? gameMap.width / 2 - 1 : gameMap.width;
                limitAreaFromY = game.players.Count == 2 || game.me.shipyard.position.y < gameMap.height / 2 ? 0 : gameMap.height / 2;
                limitAreaToY = game.players.Count == 2 || game.me.shipyard.position.y > gameMap.height / 2 ? gameMap.height : gameMap.height / 2 - 1;
            }

            game.Ready($"Oh Botyo CS {argStr}");

            Log.LogMessage("Successfully created bot! My Player ID is " + game.myId + ". Bot rng seed is " +
                           rngSeed + ".");
            Log.LogMessage($"Constants: {Constants.ConstantsStr}");

            haliteStats = new HaliteStats(game.myId.id);

            Stopwatch stopwatch = new Stopwatch();

            for (;;)
            {
                game.UpdateFrame();
                me = game.me;
                enemyPlayers = game.players.Where(p => p != me).ToArray();
                gameMap = game.gameMap;

                stopwatch.Restart();

                currentHalite = 0;
                dropoffHaliteAccess.Clear();
                dropoffHaliteAccess.Add(-1, 0);
                dropoffHaliteAccess.Add(-2, 0);
                foreach (var dropoff in me.dropoffs)
                {
                    dropoffHaliteAccess.Add(dropoff.Key, 0);
                }
                for (int x = 0; x < gameMap.width; x++)
                {
                    for (int y = 0; y < gameMap.height; y++)
                    {
                        var halite = gameMap.At(new Position(x, y)).halite;
                        currentHalite += halite;
                        dropoffHaliteAccess[scoreMap[y][x].Dropoff.Id] += (int)
                            Math.Ceiling(halite / (scoreMap[y][x].Dropoff.Distance + 1d));
                    }
                }

                foreach (var dha in dropoffHaliteAccess)
                {
                    Log.LogMessage($"Dropoff {dha.Key} has access to {dha.Value} halite");
                }

                haliteStats.LogCurrentHalite(currentHalite);
                foreach (var ship in me.ships)
                {
                    bool atDropoff = gameMap.At(ship.Value.position).structure?.owner.id == me.id.id;
                    haliteStats.LogShipHalite(ship.Key, ship.Value.halite, atDropoff, game.turnNumber);
                }

                //Log.LogMessage($"Estimated ship revenue {haliteStats.EstimateShipRevenue(game.turnNumber, Constants.MAX_TURNS)}");

                //harvestedHalitePerTurn.Add(lastHalite - currentHalite);
                //shipCountPerTurn.Add(game.players.Sum(p => p.ships.Count));

                //if (game.turnNumber > 5)
                //{
                //    var rollingHal = harvestedHalitePerTurn.TakeLast(5).Average();
                //    var rollingHalPerShip = rollingHal / shipCountPerTurn.TakeLast(5).Average();
                //    Log.LogMessage(
                //        $"{rollingHal:F} harvested ({rollingHal / currentHalite:P}) ({rollingHalPerShip:F} per ship)");
                //}

                if (game.turnNumber > playUntilTurn + 20)
                {
                    game.EndTurn(new List<Command>());
                    continue;
                }

                var commandQueue = new Dictionary<int, Command>();
                var shipsToCommand = me.ships;
                var retries = 0;
                finalReturn = game.turnNumber > Constants.MAX_TURNS - 40;
                if (game.turnNumber > playUntilTurn) finalReturn = true;
                ShipPositions.Clear();

                UpdateDropoffMap();
                if (relocateShipsNextTurn)
                {
                    RelocateShips();
                    relocateShipsNextTurn = false;
                }

                foreach (var shipToRelocate in relocateShips.ToArray())
                {
                    if (shipToRelocate.Value == -3 || (shipToRelocate.Value == -2 && buildDropoffPosition == null))
                    {
                        relocateShips[shipToRelocate.Key] = me.dropoffs.Max(d => d.Key);
                    }

                    if (me.ships.ContainsKey(shipToRelocate.Key)
                        && scoreMap[me.ships[shipToRelocate.Key].position.y][me.ships[shipToRelocate.Key].position.x]
                            .Dropoff.Id == shipToRelocate.Value)
                    {
                        relocateShips.Remove(shipToRelocate.Key);
                        Log.LogMessage(
                            $"Successfully relocated ship {shipToRelocate.Key} to dropoff {shipToRelocate.Value}");
                    }
                }

                UpdateBoostMap();
                HarvestTargets.Clear();
                priorityQueue.Clear();
                positionMap = new bool[gameMap.width, gameMap.height];
                
                bool planningDropoff = false;
                bool dropoffJustBuilt = false;

                while (shipsToCommand.Any() && retries <= 10)
                {
                    possibleConflicts.Clear();

                    // Build new dropoff
                    if (retries == 0 && !finalReturn && me.dropoffs.Count < maxDropoffs
                        && me.ships.Count >= shipsToFirstDropoff + me.dropoffs.Count * shipsToNextDropoff
                        && game.turnNumber <= Constants.MAX_TURNS - 120
                        && (!ShipStatuses.Any(s => s.Value == ShipStatus.Building)
                            || !me.ships.ContainsKey(ShipStatuses.First(s => s.Value == ShipStatus.Building).Key)))
                    {
                        var plannedPosition = FindNewDropoffPosition();

                        if (plannedPosition != null)
                        {
                            Log.LogMessage($"Planning to build dropoff at {plannedPosition}");
                            planningDropoff = true;

                            var builderShip = shipsToCommand.FirstOrDefault(s =>
                                ShipStatuses.ContainsKey(s.Key) &&
                                ShipStatuses[s.Key] == ShipStatus.Building);
                            if (builderShip.Value == null)
                                builderShip = shipsToCommand.OrderBy(s =>
                                    gameMap.CalculateDistance(s.Value.position, plannedPosition)).First();

                            if (me.halite + gameMap.At(plannedPosition).halite + builderShip.Value.halite >=
                                Constants.DROPOFF_COST)
                            {
                                buildDropoffPosition = plannedPosition;
                                ShipStatuses[builderShip.Key] = ShipStatus.Building;
                                Log.LogMessage($"Designating ship {builderShip.Value} as builder");

                                // Recalculate score map
                                UpdateDropoffMap();
                            }
                        }
                        else
                        {
                            // No more good spots
                            //maxDropoffs = 0;
                            Log.LogMessage("No good spot for dropoff");
                        }
                    }

                    // Clear builders if no current dropoff position
                    if (buildDropoffPosition == null)
                    {
                        foreach (var stat in ShipStatuses.Where(s => s.Value == ShipStatus.Building).ToArray())
                        {
                            ShipStatuses.Remove(stat.Key);
                            Log.LogMessage($"Unassigning ship {stat.Key} as builder because dropoff location is no longer available");
                        }
                    }

                    // Return ships to dropoffs in the last turns
                    if (finalReturn)
                    {
                        foreach (var ship in shipsToCommand)
                        {
                            if (game.turnNumber > playUntilTurn ||
                                gameMap.CalculateDistance(ship.Value.position,
                                    GetClosestDropoffPosition(ship.Value.position)) >
                                Constants.MAX_TURNS - game.turnNumber - 10)
                            {
                                ShipStatuses[ship.Key] = ShipStatus.Returning;
                                Log.LogMessage($"Ship {ship.Value} is going for final return");
                            }
                        }
                    }

                    // Send ships to gather
                    foreach (var ship in shipsToCommand)
                    {
                        if ((!ShipStatuses.ContainsKey(ship.Key)
                             || (ShipStatuses[ship.Key] == ShipStatus.Returning &&
                                 gameMap.At(ship.Value.position).structure?.owner.id == me.id.id)))
                        {
                            ShipStatuses[ship.Key] = finalReturn ? ShipStatus.Returning : ShipStatus.Exploring;
                            Log.LogMessage($"Ship {ship.Value} is {ShipStatuses[ship.Key]}");
                        }
                    }

                    // Manage currently gathering ships
                    foreach (var ship in shipsToCommand)
                    {
                        if (ShipStatuses[ship.Key] == ShipStatus.Gathering)
                        {
                            if (ship.Value.halite >= minHaliteToReturn || ResumeReturning.Contains(ship.Key)) // Full & returning
                            {
                                ShipStatuses[ship.Key] = ShipStatus.Returning;
                                Log.LogMessage($"Ship {ship.Value} is full and returning");
                                ResumeReturning.Remove(ship.Key);
                            }
                            else //if (gameMap.At(ship.Value.position).halite < minHaliteToHarvest) // Moving to another
                            {
                                ShipStatuses[ship.Key] = ShipStatus.Exploring;
                                Log.LogMessage($"Ship {ship.Value} is exploring");
                            }
                        }

                        if (ship.Value.halite < gameMap.At(ship.Value.position).halite * 0.1) // Not enough to move
                        {
                            ShipStatuses[ship.Key] = ShipStatus.Gathering;
                            Log.LogMessage($"Ship {ship.Value} is gathering because not enough halite to move");
                        }

                        if (ShipStatuses[ship.Key] == ShipStatus.Gathering) // Keep gathering
                        {
                            ShipPositions[ship.Key] = ship.Value.position;
                            positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                        }
                    }

                    // Manage returning ships
                    foreach (var ship in shipsToCommand
                        .Where(s => ShipStatuses[s.Key] == ShipStatus.Returning
                                    && !commandQueue.ContainsKey(s.Key))
                        .OrderBy(s => gameMap.CalculateDistance(s.Value.position,
                                          GetClosestDropoffPosition(s.Value.position)) > 1)
                        .ThenByDescending(sc => sc.Value.halite))
                    {
                        ReturnShip(ship, commandQueue);
                    }

                    // Manage builders
                    foreach (var ship in shipsToCommand.Where(s => ShipStatuses[s.Key] == ShipStatus.Building
                                                                   && !commandQueue.ContainsKey(s.Key)))
                    {
                        if (gameMap.At(buildDropoffPosition).HasStructure()
                            || DirectionExtensions.ALL_CARDINALS.Any(d =>
                                gameMap.At(buildDropoffPosition.DirectionalOffset(d)).HasStructure()))
                        {
                            // Someone already built something there (or close)
                            Log.LogMessage($"Cancelling build plans because someone already built something close");

                            buildDropoffPosition = null;
                            ShipStatuses[ship.Key] = ShipStatus.Exploring;
                            break;
                        }

                        if (ship.Value.position == buildDropoffPosition)
                        {
                            if (me.halite + ship.Value.halite + gameMap.At(buildDropoffPosition).halite >=
                                Constants.DROPOFF_COST)
                            {
                                commandQueue.Add(ship.Key, ship.Value.MakeDropoff());

                                relocateShipsNextTurn = true;
                                buildDropoffPosition = null;
                                Log.LogMessage($"Ship {ship.Value} is building at {buildDropoffPosition}");
                                dropoffJustBuilt = true;

                                haliteStats.LogDropoffBuilt(game.turnNumber);
                            }
                            else
                            {
                                Log.LogMessage($"Ship {ship.Value} is waiting for halite to build at {buildDropoffPosition}");
                                ShipPositions[ship.Key] = ship.Value.position;
                                positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                            }
                        }
                        else
                        {
                            var direction = FindDirection(ship.Value.position, buildDropoffPosition,
                                ShipStatus.Building);

                            if (direction != Direction.STILL)
                            {
                                var newPos = gameMap.Normalize(ship.Value.position.DirectionalOffset(direction));
                                ShipPositions[ship.Key] = newPos;
                                positionMap[newPos.x, newPos.y] = true;
                                commandQueue.Add(ship.Key, ship.Value.Move(direction));
                                Log.LogMessage(
                                    $"Ship {ship.Value} is going {direction} to build position {buildDropoffPosition}");
                                HarvestTargets.Add(buildDropoffPosition);
                            }
                            else
                            {
                                Log.LogMessage(
                                    $"Ship {ship.Value} is unable to move to build position {buildDropoffPosition}");
                                ShipStatuses[ship.Key] = ShipStatus.Gathering;
                                ShipPositions[ship.Key] = ship.Value.position;
                                positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                            }
                        }
                    }

                    // Manage exploring ships
                    foreach (var ship in shipsToCommand
                        .Where(s => ShipStatuses[s.Key] == ShipStatus.Exploring && !commandQueue.ContainsKey(s.Key)
                                    && priorityQueue.Contains(s.Key))
                        .OrderBy(s => priorityQueue.IndexOf(s.Key)))
                    {
                        var explorePos = GetHarvestPosition(ship.Value, out _);
                        Log.LogMessage($"Sending ship {ship} with priority to explore {explorePos}");
                        SendExplorer(ship, explorePos, commandQueue);
                    }

                    Dictionary<string, List<Tuple<Ship, double>>> posScores =
                        new Dictionary<string, List<Tuple<Ship, double>>>();
                    var explorerShips = shipsToCommand
                        .Where(s => ShipStatuses[s.Key] == ShipStatus.Exploring && !commandQueue.ContainsKey(s.Key))
                        .Select(s => s.Value)
                        .ToList();
                    int pass = 1;
                    while (explorerShips.Any())
                    {
                        Log.LogMessage($"Getting harvest targets for {explorerShips.Count}. Pass {pass}");
                        posScores.Clear();
                        foreach (var ship in explorerShips)
                        {
                            var hPos = GetHarvestPosition(ship, out var hScore, pass > 1);
                            var hPosStr = $"{hPos.x}|{hPos.y}";

                            if (!posScores.ContainsKey(hPosStr))
                                posScores.Add(hPosStr, new List<Tuple<Ship, double>>());
                            posScores[hPosStr].Add(new Tuple<Ship, double>(ship, hScore));
                        }

                        foreach (var posScore in posScores)
                        {
                            var explorePos = new Position(int.Parse(posScore.Key.Split('|')[0]),
                                int.Parse(posScore.Key.Split('|')[1]));
                            foreach (var ship in posScore.Value.OrderByDescending(v => v.Item2))
                            {
                                var successs = SendExplorer(new KeyValuePair<int, Ship>(ship.Item1.id.id, ship.Item1), 
                                    explorePos, commandQueue);
                                Log.LogMessage($"Sending ship {ship.Item1} to explore {explorePos} ({ship.Item2:F}, {successs})");
                                explorerShips.Remove(ship.Item1);
                                if (successs) break;
                            }
                        }

                        pass++;
                    }

                    // Check for conflicts
                    foreach (var ship in me.ships.Where(s =>
                        ShipStatuses.ContainsKey(s.Key) && ShipStatuses[s.Key] == ShipStatus.Gathering))
                    {
                        if (finalReturn && gameMap.At(ship.Value.position).structure?.owner.id == me.id.id)
                            continue;

                        foreach (var shipPosition in ShipPositions)
                        {
                            if (shipPosition.Key != ship.Key && Equals(shipPosition.Value, ship.Value.position))
                            {
                                possibleConflicts.Add(shipPosition.Key);
                                Log.LogMessage(
                                    $"Ship {shipPosition.Key} had conflicting destination with {ship.Value}. Retrying...");
                            }
                        }
                    }

                    // Retry conflicted ships
                    var retryShips = new Dictionary<int, Ship>();
                    foreach (var ship in me.ships)
                    {
                        if (!possibleConflicts.Contains(ship.Key)) continue;
                        retryShips.Add(ship.Key, ship.Value);
                        ShipPositions.Remove(ship.Key);
                        commandQueue.Remove(ship.Key);
                    }

                    shipsToCommand = retryShips;
                    retries++;
                }

                // Build new ships
                if (me.halite >= Constants.SHIP_COST
                    //&& game.turnNumber <= buildUntilTurn
                    //&& totalShips < maxShips
                    //&& (currentHalite / (double) totalHalite >= buildUntilHaliteLeft)
                    //&& (me.ships.Count == 0 || currentHalite / me.ships.Count >= buildUntilHalitePerShip)
                    //&& harvestedHalitePerTurn.TakeLast(5).Average() / currentHalite < 0.1
                    && (game.turnNumber < 150 || 
                        haliteStats.EstimateShipRevenue(game.turnNumber, Constants.MAX_TURNS) > Constants.SHIP_COST)
                    && !ShipPositions.ContainsValue(me.shipyard.position)

                    && ((!ShipStatuses.ContainsValue(ShipStatus.Building)
                         && !dropoffJustBuilt && !planningDropoff)
                        || me.halite + (buildDropoffPosition != null ? gameMap.At(buildDropoffPosition).halite : 0) >
                        Constants.DROPOFF_COST + Constants.SHIP_COST))
                {
                    commandQueue.Add(-1, me.shipyard.Spawn());
                    Log.LogMessage($"Building new ship.");
                }

                game.EndTurn(commandQueue.Values);
                Log.LogMessage($"Elapsed time {stopwatch.ElapsedMilliseconds} ms");
            }
        }

        private static void ReturnShip(KeyValuePair<int, Ship> ship, Dictionary<int, Command> commandQueue)
        {
            var dropoffPos = GetClosestDropoffPosition(ship.Value.position);

            var direction = FindDirection(ship.Value.position, dropoffPos, ShipStatus.Returning);
            if (finalReturn && ship.Value.position == dropoffPos) return; // All good
            if (direction != Direction.STILL)
            {
                var newPos = gameMap.Normalize(ship.Value.position.DirectionalOffset(direction));
                ShipPositions[ship.Key] = newPos;
                positionMap[newPos.x, newPos.y] = true;
                commandQueue.Add(ship.Key, ship.Value.Move(direction));
                Log.LogMessage($"Ship {ship.Value} is returning {direction} to dropoff {dropoffPos}");

                if (gameMap.At(newPos).ship?.owner.id == me.id.id
                    && !(finalReturn && newPos == dropoffPos))
                {
                    if (newPos == dropoffPos ||
                        (gameMap.CalculateDistance(newPos, dropoffPos) == 1 &&
                         (ShipStatuses[gameMap.At(newPos).ship.id.id] == ShipStatus.Exploring ||
                          ShipStatuses[gameMap.At(newPos).ship.id.id] == ShipStatus.Gathering)))
                    {
                        // Move other ship from dropoff
                        var swapShip = gameMap.At(newPos).ship;
                        if (swapShip.id.id != ship.Key && !commandQueue.ContainsKey(swapShip.id.id))
                        {
                            if (DirectionExtensions.ALL_CARDINALS.All(d =>
                                gameMap.At(swapShip.position.DirectionalOffset(d)).ship != null))
                            {
                                // Swap if ship cannot move
                                ShipStatuses[swapShip.id.id] = ShipStatus.Exploring;
                                ShipPositions[swapShip.id.id] = ship.Value.position;
                                positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                                commandQueue.Add(swapShip.id.id, swapShip.Move(direction.InvertDirection()));
                                Log.LogMessage($"Ship {swapShip} is swapping place with {ship.Value}");

                                var conflictingShip = ShipPositions.FirstOrDefault(sp =>
                                    sp.Key != swapShip.id.id && sp.Value == ship.Value.position);
                                if (conflictingShip.Value != null)
                                    possibleConflicts.Add(conflictingShip.Key);
                            }
                            else
                            {
                                ShipStatuses[swapShip.id.id] = ShipStatus.Exploring;
                                var explorePos = GetHarvestPosition(ship.Value, out _);
                                SendExplorer(new KeyValuePair<int, Ship>(swapShip.id.id, swapShip), explorePos, commandQueue);
                            }
                        }
                    }
                    else
                    {
                        priorityQueue.Add(gameMap.At(newPos).ship.id.id);
                    }
                }
            }
            else
            {
                ShipStatuses[ship.Key] = ShipStatus.Gathering;
                ShipPositions[ship.Key] = ship.Value.position;
                positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                ResumeReturning.Add(ship.Key);
                Log.LogMessage($"Ship {ship.Value} was unable to move to dropoff");
            }
        }

        private static bool SendExplorer(KeyValuePair<int, Ship> ship, Position explorePos,
            Dictionary<int, Command> commandQueue)
        {
            if (Equals(explorePos, ship.Value.position))
            {
                ShipStatuses[ship.Key] = ShipStatus.Gathering;
                ShipPositions[ship.Key] = ship.Value.position;
                positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                if (LastHarvestTargets.ContainsKey(ship.Key))
                    LastHarvestTargets.Remove(ship.Key);
            }
            else
            {
                var direction = FindDirection(ship.Value.position, explorePos, ShipStatus.Exploring);

                if (direction != Direction.STILL)
                {
                    var newPos = gameMap.Normalize(ship.Value.position.DirectionalOffset(direction));
                    ShipPositions[ship.Key] = newPos;
                    positionMap[newPos.x, newPos.y] = true;
                    commandQueue.Add(ship.Key, ship.Value.Move(direction));
                    HarvestTargets.Add(explorePos);

                    if (LastHarvestTargets.ContainsKey(ship.Key))
                        LastHarvestTargets.Remove(ship.Key);
                    LastHarvestTargets.Add(ship.Key, explorePos);
                }
                else
                {
                    ShipStatuses[ship.Key] = ShipStatus.Gathering;
                    ShipPositions[ship.Key] = ship.Value.position;
                    positionMap[ship.Value.position.x, ship.Value.position.y] = true;
                    if (LastHarvestTargets.ContainsKey(ship.Key))
                        LastHarvestTargets.Remove(ship.Key);
                    return false;
                }
            }

            return true;
        }

        public static Position GetClosestDropoffPosition(Position origin)
        {
            if (!me.dropoffs.Any()) return me.shipyard.position;
            var dropoffs = me.dropoffs.Select(d => d.Value.position).Append(me.shipyard.position);
            var closestDropoffPosition = dropoffs.OrderBy(d => gameMap.CalculateDistance(origin, d)).First();
            return closestDropoffPosition;
        }

        public static void UpdateDropoffMap()
        {
            for (int x = 0; x < gameMap.width; x++)
            {
                for (int y = 0; y < gameMap.height; y++)
                {
                    scoreMap[y][x].Dropoff = new DropoffCellInfo();
                }
            }

            List<Position> pq = new List<Position> { me.shipyard.position };
            scoreMap[me.shipyard.position.y][me.shipyard.position.x].Dropoff.Id = -1;
            scoreMap[me.shipyard.position.y][me.shipyard.position.x].Dropoff.Distance = 0;
            foreach (var dropoff in me.dropoffs.Values)
            {
                pq.Add(dropoff.position);
                scoreMap[dropoff.position.y][dropoff.position.x].Dropoff.Id = dropoff.id.id;
                scoreMap[dropoff.position.y][dropoff.position.x].Dropoff.Distance = 0;
            }

            if (buildDropoffPosition != null)
            {
                pq.Add(buildDropoffPosition);
                scoreMap[buildDropoffPosition.y][buildDropoffPosition.x].Dropoff.Id = -2;
                scoreMap[buildDropoffPosition.y][buildDropoffPosition.x].Dropoff.Distance = 0;
            }

            while (pq.Any())
            {
                foreach (var p in pq.ToArray())
                {
                    pq.Remove(p);
                    var dcInfo = scoreMap[p.y][p.x].Dropoff;
                    var fuel = dcInfo.Fuel + gameMap.cells[p.y][p.x].halite * .1;

                    foreach (var dir in DirectionExtensions.ALL_CARDINALS)
                    {
                        var np = gameMap.Normalize(p.DirectionalOffset(dir));
                        var nDcInfo = scoreMap[np.y][np.x].Dropoff;

                        if (nDcInfo.Distance == -1
                            || nDcInfo.Distance > dcInfo.Distance + 1
                            || (nDcInfo.Id == dcInfo.Id && nDcInfo.Fuel > fuel))
                        {
                            nDcInfo.Id = dcInfo.Id;
                            nDcInfo.Distance = dcInfo.Distance + 1;
                            nDcInfo.Fuel = fuel;
                            pq.Add(np);
                        }
                    }
                }
            }
        }

        public static void UpdateShipFuelMap(Ship ship)
        {
            Log.LogMessage($"Updating ship fuel map for ship {ship}");

            if (!shipFuelInfo.ContainsKey(ship.id.id))
                shipFuelInfo.Add(ship.id.id, new Dictionary<int, ShipCellInfo>());
            else
                shipFuelInfo[ship.id.id].Clear();

            var fuelInfo = shipFuelInfo[ship.id.id];
            var initialScInfo = new ShipCellInfo { Distance = 0 };
            fuelInfo.Add(ship.position.y * 100 + ship.position.x, initialScInfo); 

            List<Position> pq = new List<Position> { ship.position };

            int count = 0;
            while (pq.Any())
            {
                foreach (var p in pq.ToArray())
                {
                    pq.Remove(p);
                    var scInfo = fuelInfo[p.y * 100 + p.x];
                    var fuel = scInfo.Fuel + gameMap.cells[p.y][p.x].halite * .1;
                    if (fuel > ship.halite) continue;

                    foreach (var dir in DirectionExtensions.ALL_CARDINALS)
                    {
                        var np = gameMap.Normalize(p.DirectionalOffset(dir));

                        if (!fuelInfo.TryGetValue(np.y * 100 + np.x, out var nScInfo))
                        {
                            nScInfo = new ShipCellInfo();
                            fuelInfo.Add(np.y * 100 + np.x, nScInfo);
                        }

                        if (nScInfo.Distance == -1 ||
                            nScInfo.Fuel > fuel)
                        {
                            nScInfo.Distance = scInfo.Distance + 1;
                            nScInfo.Fuel = fuel;
                            pq.Add(np);
                        }
                    }
                }

                count++;

                // TODO: See if this value is good enough
                if (count > 50) break;
            }
        }

        public static void UpdateBoostMap()
        {
            boostMap.Clear();

            for (int x = 0; x < gameMap.width; x++)
            {
                for (int y = 0; y < gameMap.height; y++)
                {
                    int key = y * 100 + x;
                    boostMap.Add(key, EnemyShipsNear(new Position(x, y), 4).Count() >= 2);
                }
            }
        }

        public static Position GetHarvestPosition(Ship ship, out double bestScore, bool reuseMap = false)
        {
            bestScore = double.MinValue;
            var origin = ship.position;
            var bestPosition = origin;

            if (!reuseMap)
                UpdateShipFuelMap(ship);

            // TODO: Optimize this (update on the fly?)
            Dictionary<int, int> harvestTargetsCache = new Dictionary<int, int>();
            foreach (var target in HarvestTargets)
            {
                int key = target.y * 100 + target.x;
                if (!harvestTargetsCache.ContainsKey(key))
                    harvestTargetsCache.Add(key, 1);
                else
                    harvestTargetsCache[key]++;
            }

            for (int x = 0; x < gameMap.width; x++)
            {
                for (int y = 0; y < gameMap.height; y++)
                {
                    if (limitToOwnArea && (x < limitAreaFromX || x > limitAreaToX || y < limitAreaFromY ||
                                           y > limitAreaToY)) continue;

                    var position = new Position(x, y);

                    var dcInfo = scoreMap[y][x].Dropoff;

                    bool relocate = relocateShips.ContainsKey(ship.id.id) && dcInfo.Id == relocateShips[ship.id.id];

                    ShipCellInfo scInfo;
                    if (relocate)
                    {
                        scInfo = new ShipCellInfo {Distance = 1, Fuel = 0};
                    }
                    else
                    {
                        if (!shipFuelInfo.TryGetValue(ship.id.id, out var fuelInfo)) continue;
                        if (!fuelInfo.TryGetValue(y * 100 + x, out scInfo)) continue;
                    }
                    //{
                    //    // Boost position for new dropoffs
                    //    if (boostPositionTimer > 0 && boostPosition != null)
                    //    {
                    //        var bDist = gameMap.CalculateDistance(position, boostPosition);
                    //        if (bDist <= 10)
                    //            scInfo = new ShipCellInfo {Distance = 1, Fuel = 0};
                    //        else continue;
                    //    }
                    //    else continue;
                    //}

                    var mapCell = gameMap.At(position);
                    var cellHalite = mapCell.halite;
                    if (cellHalite <= 0) continue;

                    var halite = -scInfo.Fuel;
                    double moves = scInfo.Distance;

                    var boost = boostMap[y * 100 + x];

                    int harvestedHalite = 0;
                    int harvestMoves = 0;

                    for (; ; )
                    {
                        int newHalite = (int)Math.Ceiling(cellHalite * .25);
                        if (boost) newHalite *= 2;
                        if (ship.halite + harvestedHalite + newHalite > 1000)
                            newHalite = 1000 - ship.halite - harvestedHalite;

                        if ((cellHalite > 0 && (int)moves == 0) ||
                            (int) Math.Ceiling((halite + newHalite) / (moves + 1)) > (int)Math.Ceiling(halite / moves))
                        {
                            halite += newHalite;
                            harvestedHalite += newHalite;
                            cellHalite -= (int)Math.Ceiling(cellHalite * .25);
                            moves++;
                            harvestMoves++;
                        }
                        else
                            break;
                    }

                    // Add prorated fuel/distance to dropoff
                    //dcInfo.Fuel -= (mapCell.halite - cellHalite) * .1;
                    halite += -dcInfo.Fuel * (harvestedHalite / 1000d);
                    moves += dcInfo.Distance * (harvestedHalite / 1000d);

                    var score = (int) moves == 0 ? 0 : halite / moves;

                    //if (ship.id.id == 41)
                    //    Log.LogMessage($"Score for {position} is {score} -> ({harvestedHalite} hal in {harvestMoves}m) - ({scInfo.Fuel} fuel in {scInfo.Distance}m) - ({dcInfo.Fuel} fuel in {dcInfo.Distance}m * {harvestedHalite / 1000d:F})");

                    if (score > 0)
                    {
                        if (origin != position && positionMap[x, y]
                                               && ShipStatuses[ShipPositions.First(p => p.Value == position).Key] !=
                                               ShipStatus.Returning)
                        {
                            score /= 200;
                        }

                        if (mapCell.ship != null && mapCell.ship.owner.id != me.id.id)
                        {
                            // TODO: Check if good idea to crash - e.g. closer to my base and ships rather than his

                            //var enemyShipHalite = Math.Min(1000, mapCell.ship.halite + mapCell.halite * .25);
                            //if (game.players.Count == 2 && enemyShipHalite >= 500 && enemyShipHalite > ship.halite * 2
                            //    && scInfo.Distance <= 2)
                            //    score *= 2;
                            //else
                            //    score /= 200;

                            if (game.players.Count == 4 ||
                                enemyPlayers[0].ships.Count + 5 >= me.ships.Count ||
                                //enemyPlayers[0].halite > me.halite ||
                                mapCell.ship.halite < ship.halite)
                                score /= 200;
                        }

                        if (ship.halite >= 700)
                            foreach (var dir in DirectionExtensions.ALL_CARDINALS)
                            {
                                var enemyShip = gameMap.At(position.DirectionalOffset(dir)).ship;
                                if (enemyShip == null || enemyShip.owner.id == me.id.id) continue;
                                if (enemyShip.halite < ship.halite)
                                    score /= 2;
                            }

                        int htcKey = position.y * 100 + position.x;
                        if (harvestTargetsCache.ContainsKey(htcKey))
                            score /= Math.Pow(2, harvestTargetsCache[htcKey]);
                    }

                    // Test to see if this actually helps
                    //if (LastHarvestTargets.ContainsKey(ship.id.id)
                    //    && LastHarvestTargets[ship.id.id] == position)
                    //    score *= 1.5;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPosition = position;
                    }

                    if (origin == position)
                        Log.LogMessage($"Current score for ship {ship} is {score:F}");
                }
            }

            return bestPosition;
        }

        public static IEnumerable<Tuple<Ship, int>> EnemyShipsNear(Position position, int distance)
        {
            List<Tuple<Ship, int>> ships = new List<Tuple<Ship, int>>();

            for (int xOffset = -distance; xOffset <= distance; xOffset++)
            {
                for (int yOffset = -distance + Math.Abs(xOffset); yOffset <= distance - Math.Abs(xOffset); yOffset++)
                {
                    var testPos = new Position(position.x + xOffset, position.y + yOffset);
                    var ship = gameMap.At(testPos).ship;
                    if (ship != null && ship.owner.id != me.id.id)
                    {
                        if (limitToOwnArea && (testPos.x < limitAreaFromX || testPos.x > limitAreaToX ||
                                               testPos.y < limitAreaFromY || testPos.y > limitAreaToY)) continue;

                        ships.Add(new Tuple<Ship, int>(ship, Math.Abs(xOffset) + Math.Abs(yOffset)));
                    }
                }
            }

            return ships;
        }

        public static Direction FindDirection(Position source, Position target, ShipStatus shipStatus)
        {
            if (source == target) return Direction.STILL;

            const int ORIGIN = 0;
            const int UNEXPLORED = -1;
            const int FRIENDLY_SHIP = -2;
            const int DESTINATION = -3;
            const int ENEMY_SHIP = -4;
            const int ENEMY_STRUCTURE = -5;
            const int SHIPYARD = -6;

            bool sourceIsDropoff = gameMap.cells[source.y][source.x].structure?.owner.id == me.id.id;

            Tuple<Position, int, double>[,] moveMap = new Tuple<Position, int, double>[gameMap.width, gameMap.height];

            for (int x = 0; x < gameMap.width; x++)
            {
                for (int y = 0; y < gameMap.height; y++)
                {
                    moveMap[x, y] = new Tuple<Position, int, double>(new Position(0, 0), UNEXPLORED, 0);
                    if (gameMap.cells[y][x].ship != null &&
                        gameMap.cells[y][x].ship.owner.id != me.id.id &&
                        gameMap.cells[y][x].structure?.owner.id != me.id.id)
                    {
                        moveMap[x, y] = new Tuple<Position, int, double>(new Position(0, 0), ENEMY_SHIP, 0);
                    }

                    if (shipStatus == ShipStatus.Returning && gameMap.cells[y][x].structure != null &&
                        gameMap.cells[y][x].structure.owner.id != me.id.id)
                    {
                        moveMap[x, y] = new Tuple<Position, int, double>(new Position(0, 0), ENEMY_STRUCTURE, 0);
                    }
                }
            }

            foreach (var shipPosition in ShipPositions.Values)
            {
                moveMap[shipPosition.x, shipPosition.y] =
                    new Tuple<Position, int, double>(new Position(0, 0), FRIENDLY_SHIP, 0);
            }

            moveMap[source.x, source.y] = new Tuple<Position, int, double>(new Position(0, 0), ORIGIN, 0);
            moveMap[target.x, target.y] = new Tuple<Position, int, double>(new Position(0, 0), DESTINATION, 0);

            List<Position> fPos = new List<Position> { source };
            var move = 1;
            var foundAtMove = 0;
            while (fPos.Any())
            {
                foreach (var c in fPos.ToArray())
                {
                    fPos.Remove(c);
                    if (limitToOwnArea && (c.x < limitAreaFromX || c.x > limitAreaToX || c.y < limitAreaFromY ||
                                           c.y > limitAreaToY)) continue;

                    foreach (var direction in DirectionExtensions.ALL_CARDINALS)
                    {
                        var newPos = gameMap.Normalize(new Position(c.x, c.y).DirectionalOffset(direction));
                        var score = moveMap[c.x, c.y].Item3 + 100;
                        if (shipStatus == ShipStatus.Building || shipStatus == ShipStatus.Returning)
                        {
                            score += gameMap.cells[c.y][c.x].halite * .1;
                            if (shipStatus == ShipStatus.Returning && EnemyShipsNear(newPos, 1).Any())
                                score += 500;
                        }
                        else
                        {
                            score += gameMap.cells[c.y][c.x].halite * .1;

                            if (newPos == me.shipyard.position)
                                score += 1000;
                        }

                        if (gameMap.cells[c.y][c.x].structure != null &&
                            gameMap.cells[c.y][c.x].structure.owner.id != me.id.id)
                            score += 500;

                        if (newPos == target
                            && ((int) moveMap[newPos.x, newPos.y].Item3 == 0 ||
                                moveMap[newPos.x, newPos.y].Item3 > score))
                        {
                            moveMap[newPos.x, newPos.y] =
                                new Tuple<Position, int, double>(new Position(c.x, c.y), DESTINATION, score);
                            foundAtMove = move;
                        }
                        else if (moveMap[newPos.x, newPos.y].Item2 == UNEXPLORED ||
                            (moveMap[newPos.x, newPos.y].Item2 > 0 && moveMap[newPos.x, newPos.y].Item3 > score))
                        {
                            moveMap[newPos.x, newPos.y] =
                                new Tuple<Position, int, double>(new Position(c.x, c.y), move, score);
                            fPos.Add(newPos);
                        }
                    }
                }

                move++;
                if ((foundAtMove > 0 && move > foundAtMove + 10) || move > 100) break;
            }

            // No path exists so just try to move closer
            if (foundAtMove == 0)
            {
                var fastDirs = gameMap.GetUnsafeMoves(source, target)
                    .Select(d => new { dir = d, pos = gameMap.Normalize(source.DirectionalOffset(d)) })
                    .OrderBy(np => gameMap.At(np.pos).ship != null && gameMap.At(np.pos).ship.owner.id != me.id.id)
                    .ThenBy(np => gameMap.CalculateDistance(target, np.pos))
                    .ThenBy(np => gameMap.At(np.pos).halite).ToArray();
                foreach (var newPos in fastDirs)
                {
                    if (limitToOwnArea && (newPos.pos.x < limitAreaFromX || newPos.pos.x > limitAreaToX ||
                                           newPos.pos.y < limitAreaFromY || newPos.pos.y > limitAreaToY)) continue;

                    // Don't go if my ship is already there (unless final return - then crash into base)
                    if (ShipPositions.Any(sp => sp.Value.Equals(newPos.pos)) &&
                        !(finalReturn && gameMap.At(newPos.pos).structure?.owner.id == me.id.id))
                        continue;

                    // Don't go if enemy ship is there (unless enemy ship at/blocked my base - then crash)
                    if (gameMap.At(newPos.pos).ship != null
                        && gameMap.At(newPos.pos).ship.owner.id != me.id.id
                        && gameMap.At(newPos.pos).structure?.owner.id != me.id.id
                        && !(sourceIsDropoff && gameMap.CalculateDistance(source, newPos.pos) == 1))
                        continue;

                    return newPos.dir;
                }

                return Direction.STILL;
            }

            var tracePos = new Position(target.x, target.y);
            var lastDirection = Direction.STILL;

            while (tracePos != source)
            {
                lastDirection = DirectionExtensions.ALL_CARDINALS.First(dir =>
                    gameMap.Normalize(moveMap[tracePos.x, tracePos.y].Item1.DirectionalOffset(dir)) == tracePos);
                tracePos = moveMap[tracePos.x, tracePos.y].Item1;
            }

            var nextPosition = gameMap.Normalize(source.DirectionalOffset(lastDirection));
            if (ShipPositions.ContainsValue(nextPosition)
                && !(finalReturn && gameMap.At(nextPosition).structure?.owner.id == me.id.id))
                return Direction.STILL;

            return lastDirection;
        }

        public static Position FindNewDropoffPosition()
        {
            int bestScore = 0;
            Position bestPos = null;

            //int fromX = me.shipyard.position.x - gameMap.width / 3;
            //int toX = me.shipyard.position.x + gameMap.width / 3;
            int fromX = 0;
            int toX = gameMap.width;
            int fromY = 0;
            int toY = gameMap.height;

            if (limitToOwnArea)
            {
                fromX = limitAreaFromX;
                toX = limitAreaToX;
            }

            //if (game.players.Count == 4)
            //{
            //    fromY = me.shipyard.position.y - gameMap.height / 3;
            //    toY = me.shipyard.position.y + gameMap.height / 3;

            //    if (limitToOwnArea)
            //    {
            //        fromY = limitAreaFromY;
            //        toY = limitAreaToY;
            //    }
            //}

            int count = 0;
            for (int x = fromX; x <= toX; x++)
            {
                for (int y = fromY; y <= toY; y++)
                {
                    var pos = gameMap.Normalize(new Position(x, y));
                    if (gameMap.cells[pos.y][pos.x].halite < 350 || gameMap.cells[pos.y][pos.x].HasStructure()) continue;
                    var dist = gameMap.CalculateDistance(pos, me.shipyard.position);
                    if (dist < 20) continue;
                    if (me.dropoffs.Any(d => gameMap.CalculateDistance(pos, d.Value.position) < 20)) continue;
                    if (!me.ships.Any(s => gameMap.CalculateDistance(pos, s.Value.position) <= 8)) continue;
                    //if (dist > 30 && me.dropoffs.All(d => gameMap.CalculateDistance(pos, d.Value.position) > 30))
                    //    continue;

                    int score = 0;
                    for (int xOffset = -DropoffRewardDistance; xOffset <= DropoffRewardDistance; xOffset++)
                    {
                        for (int yOffset = -DropoffRewardDistance + Math.Abs(xOffset);
                            yOffset <= DropoffRewardDistance - Math.Abs(xOffset);
                            yOffset++)
                        {
                            var testPos = gameMap.Normalize(new Position(pos.x + xOffset, pos.y + yOffset));

                            if (limitToOwnArea && (testPos.x < fromX || testPos.x > toX || testPos.y < fromY ||
                                                   testPos.y > toY)) continue;

                            var testDist = gameMap.CalculateDistance(testPos, pos);
                            //if (testDist >= gameMap.CalculateDistance(testPos, me.shipyard.position)) continue;
                            //if (me.dropoffs.Any(d => testDist >= gameMap.CalculateDistance(testPos, d.Value.position)))
                            //    continue;
                            var mapCell = gameMap.cells[testPos.y][testPos.x];
                            if (mapCell.structure != null && testDist <= 10) goto Skip;
                            if (mapCell.ship != null && mapCell.ship.owner.id != me.id.id) continue;
                            if (testPos == pos)
                            {
                                score += mapCell.halite;
                                if (mapCell.ship?.owner.id == me.id.id)
                                    score += mapCell.ship.halite;
                            }
                            else
                            {
                                score += (int) (mapCell.halite / (testDist + 1));
                            }
                        }
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        if (score > minScoreForDropoff)
                            bestPos = pos;
                    }

                    count++;

                    Skip:;
                }
            }
            if (bestPos != null)
                Log.LogMessage($"Best dropoff location {bestPos} (out of {count}) with score {bestScore}");
            else
                Log.LogMessage($"Best dropoff score {bestScore} {bestPos} (out of {count}) is not enough for dropoff");
                
            return bestPos;
        }

        public static void RelocateShips()
        {
            relocateShips.Clear();
            foreach (var dha in dropoffHaliteAccess)
            {
                int sNum = (int) Math.Floor(me.ships.Count *
                                            (dha.Value / (double) dropoffHaliteAccess.Sum(d => d.Value)));
                sNum -= me.ships.Values.Count(s => scoreMap[s.position.y][s.position.x].Dropoff.Id == dha.Key);
                if (sNum <= 0) continue;

                Log.LogMessage($"Requesting {sNum} ships to dropoff {dha.Key}");

                Position dropoffPos;
                switch (dha.Key)
                {
                    case -1:
                        dropoffPos = me.shipyard.position;
                        break;
                    case -2:
                        dropoffPos = buildDropoffPosition ?? me.dropoffs.Last().Value.position;
                        break;
                    default:
                        dropoffPos = me.dropoffs[dha.Key].position;
                        break;
                }

                var aShips = (from s in me.ships
                        where !relocateShips.ContainsKey(s.Key)
                              && scoreMap[s.Value.position.y][s.Value.position.x].Dropoff.Id != dha.Key
                        orderby ShipStatuses.ContainsKey(s.Key) && ShipStatuses[s.Key] == ShipStatus.Returning,
                            gameMap.CalculateDistance(s.Value.position, dropoffPos)
                        select s)
                    .Take(sNum)
                    .ToArray();

                foreach (var aShip in aShips)
                {
                    relocateShips.Add(aShip.Key, dha.Key);
                    Log.LogMessage($"Relocating ship {aShip.Key} to {dha.Key}");
                }
            }
        }
    }
}
