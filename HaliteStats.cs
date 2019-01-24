using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MyBot
{
    public class HaliteStats
    {
        public int ConsiderLastTurns = 10;
        public int BufferTurnsAtEnd = 40;
        public string logfile;

        private readonly Dictionary<int, ShipHaliteStats> ShipStats = new Dictionary<int, ShipHaliteStats>();
        private readonly List<int> MapHalite = new List<int>();
        private readonly List<double> HalitePerTurnHistory = new List<double>();
        private int dropoffBuiltTurn = 0;

        public HaliteStats(int playerId)
        {
            logfile = $"halitestats-{playerId}.log";
        }

        public void LogShipHalite(int id, int halite, bool atDropoff, int turn)
        {
            if (ShipStats.ContainsKey(id))
            {
                var ss = ShipStats[id];
                ss.moves++;
                ss.lastUpdated = turn;

                if (atDropoff && ss.halite > 0) // Return
                {
                    ss.movesToReturn.Add(ss.moves);
                    ss.halitePerRoundtrip.Add(ss.halite);
                    ss.moves = 0;
                }

                ss.halite = halite;
            }
            else
            {
                ShipStats.Add(id, new ShipHaliteStats {moves = 1, halite = halite, lastUpdated = turn});
            }
        }

        public void LogDropoffBuilt(int turn)
        {
            dropoffBuiltTurn = turn;
        }

        public double EstimateHalitePerTurn(int filterFromTurn)
        {
            //var shipsHalPerTurn = (from s in ShipStats.Values
            //                       where s.lastUpdated >= filterFromTurn
            //                             && s.halitePerRoundtrip.Any()
            //                       select s.halitePerRoundtrip.Last() / (double)s.movesToReturn.Last())
            //    .OrderBy(s => s)
            //    .ToArray();

            var stats = from s in ShipStats.Values
                where s.lastUpdated >= filterFromTurn
                      && s.halitePerRoundtrip.Any() || s.moves > 20
                select s.moves > 20
                    ? s.halite / (double) s.moves
                    : s.halitePerRoundtrip.Last() / (double) s.movesToReturn.Last();
            
            var shipsHalPerTurn = stats
                .OrderBy(s => s)
                .ToArray();

            if (!shipsHalPerTurn.Any()) return 0;
            var hpt = shipsHalPerTurn.Skip(shipsHalPerTurn.Length / 2).Take(2 - shipsHalPerTurn.Length % 2).Average();
            HalitePerTurnHistory.Add(hpt);
            return HalitePerTurnHistory.TakeLast(5).Average();
        }

        public int EstimateShipRevenue(int turn, int totalTurns)
        {
            //var result =
            //    (int) (EstimateHalitePerTurn(turn - ConsiderLastTurns) * (totalTurns - turn - BufferTurnsAtEnd));
            var result = 0d;
            var hpt = EstimateHalitePerTurn(turn - ConsiderLastTurns);
            var dpr = EstimateHaliteDepletionRatio();
            for (int i = 0; i < totalTurns - turn - BufferTurnsAtEnd; i++)
            {
                hpt -= hpt * dpr;
                result += hpt;
                dpr *= 1.003;
            }

            if (turn <= dropoffBuiltTurn + 20)
                result *= 1.1;

            File.AppendAllText(logfile, $"{turn}:{result:F} ({hpt:F}/turn - {dpr:P} dpr) {(turn <= dropoffBuiltTurn + 20 ? "*" : "")}\n");
            return (int) result;
        }

        public double EstimateHaliteDepletionRatio(int lastTurns = 31)
        {
            List<int> depl = new List<int>();
            var cHal = (MapHalite.Count >= lastTurns ? MapHalite.TakeLast(lastTurns) : MapHalite).ToArray();
            for (int i = 1; i < cHal.Length; i++)
            {
                depl.Add(cHal[i - 1] - cHal[i]);
            }

            var midDepl = depl.OrderBy(d => d).Skip(depl.Count / 2).Take(2 - depl.Count % 2).Average();

            return midDepl / cHal.Skip(cHal.Length / 2).Take(2 - cHal.Length % 2).Average();
        }

        public void LogCurrentHalite(int currentHalite)
        {
            MapHalite.Add(currentHalite);
        }
    }

    class ShipHaliteStats
    {
        public int moves;
        public int halite;
        public int lastUpdated;
        public List<int> halitePerRoundtrip = new List<int>();
        public List<int> movesToReturn = new List<int>();
    }
}
