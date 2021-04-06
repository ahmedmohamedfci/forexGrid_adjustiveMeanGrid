using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Collections.Generic;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class adjustiveMeanGrid : Robot
    {
        #region parameters

        [Parameter("grid radius", DefaultValue = 50, MinValue = 5)]
        public int PipRadius { get; set; }

        [Parameter("Volume", DefaultValue = 1000, MinValue = 1000)]
        public int Volume { get; set; }

        [Parameter("Buy", DefaultValue = true, Group = "default")]
        public bool Buy { get; set; }

        [Parameter("Sell", DefaultValue = true, Group = "default")]
        public bool Sell { get; set; }
        [Parameter("exponent increase volume", DefaultValue = true, Group = "default")]
        public bool volumeIncrease { get; set; }

        [Parameter("max spread", DefaultValue = 3, MinValue = 0, Group = "default")]
        public double MaxaSpread { get; set; }
        #endregion

        int volume;
        double startingBalance;
        DateTime startTime;
        public static Robot myRobot;
        Grid sellGrid, buyGrid;

        protected override void OnStart()
        {
            startingBalance = Account.Balance;
            volume = Volume;
            Positions.Closed += onClosePosition;
            startTime = Server.Time;
            myRobot = this;
            // Put your initialization logic here

            if (Sell)
            {
                sellGrid = new Grid(this, TradeType.Sell, MaxaSpread, PipRadius, volume);
            }
            if (Buy)
            {
                buyGrid = new Grid(this, TradeType.Buy, MaxaSpread, PipRadius, volume);
            }

        }

        protected override void OnTick()
        {
            // Put your core logic here
            if (Sell)
                sellGrid.checker();
            if (Buy)
                buyGrid.checker();
        }

        protected override void OnStop()
        {
            foreach (Position p in Positions)
            {
                p.Close();
            }
        }

        void onClosePosition(PositionClosedEventArgs obj)
        {

        }

    }

    class Grid
    {
        Robot myRobot;
        TradeType direction;
        int volume;
        double radius, maxSpread;
        List<Position> gridPositions = new List<Position>();
        TradeType Sell = TradeType.Sell, Buy = TradeType.Buy;
        public Grid(Robot robot, TradeType direc, double spreadMax, double rad, int vol)
        {
            myRobot = robot;
            direction = direc;
            volume = vol;
            radius = rad;
            maxSpread = spreadMax;
            openTrade(direction, volume);
        }

        private void openTrade(TradeType tradeType, int volume)
        {
            if (myRobot.Symbol.Spread <= maxSpread)
            {
                TradeResult trade1 = myRobot.ExecuteMarketOrder(tradeType, myRobot.Symbol.Name, volume, "");
                if (trade1.Error != null)
                    myRobot.Print(trade1.Error);
                else
                    gridPositions.Add(trade1.Position);
            }
        }

        private void closeTradeByPosition(Position p)
        {
            gridPositions.Remove(p);
            p.Close();
        }

        public bool shouldOpenAnotherLevel()
        {
            /// todo: maybe put an indicator to check if i should open this type of trade
            if (gridPositions.Count == 0)
                return true;
            Position lastPosition = gridPositions.Last();
            return lastPosition.Pips < 0 && lastPosition.Pips <= -radius;
        }

        private bool isProfitGreaterThan(double target)
        {
            double loss = target;
            Position lastPos = gridPositions.Last();
            for (int i = gridPositions.Count - 1; i >= 0; i--)
            {
                loss += gridPositions[i].NetProfit;
                if (loss >= 0)
                    return true;
            }
            if (loss >= 0)
                return true;
            return false;
        }

        private void balanceOpenAndClose(Position loser)
        {
            double loss = loser.NetProfit;
            Position lastPos = gridPositions.Last();
            while (gridPositions.Count > 0 && lastPos.NetProfit > 0)
            {
                if (lastPos.NetProfit > 0)
                {
                    loss += lastPos.NetProfit;
                    closeTradeByPosition(lastPos);
                    lastPos = gridPositions.Last();
                    if (loss >= 0)
                    {
                        closeTradeByPosition(loser);
                        break;
                    }
                }
            }
        }

        public void checker()
        {

            if (gridPositions.Count == 0)
            {
                openTrade(direction, volume);
                return;
            }
            else if (gridPositions.Count == 1)
            {
                // if only 1 trade, check if it hits take profit
                Position p = gridPositions[0];
                if (p.Pips >= radius)
                {
                    closeTradeByPosition(p);
                }
                else if (p.Pips < 0 && p.Pips <= -radius)
                {
                    openTrade(direction, volume);
                }
                return;
            }
            else if (gridPositions.Count == 2)
            {
                double totalPips = gridPositions[0].Pips + gridPositions[1].Pips;
                if (totalPips >= radius)
                {
                    closeTradeByPosition(gridPositions[1]);
                    closeTradeByPosition(gridPositions[0]);
                }
                else if (shouldOpenAnotherLevel())
                {
                    openTrade(direction, volume);
                }
            }
            else
            {
                // if we have more than 1 trade, try to balance the grid
                Position biggestLoser = gridPositions[0];
                if (shouldOpenAnotherLevel())
                {
                    openTrade(direction, volume);
                }
                else if (isProfitGreaterThan(biggestLoser.NetProfit))
                {
                    balanceOpenAndClose(biggestLoser);
                }

            }

        }


    }
}
