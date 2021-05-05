using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;
using cAlgo.Indicators;
using System.Collections.Generic;

/*
notes:
if we keep opening trades without a limit, we are bound to have a trade on the lowest low or highest high.. we need to limit this with an indicator
*/

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class adjustiveMeanGrid : Robot
    {
        #region parameters

        [Parameter("grid radius", DefaultValue = 20, MinValue = 5)]
        public int PipRadius { get; set; }

        [Parameter("take profit", DefaultValue = 20, MinValue = 5)]
        public int TakeProfit { get; set; }

        [Parameter("risk after pips", DefaultValue = 50, MinValue = 1)]
        public int riskPips { get; set; }

        [Parameter("Volume", DefaultValue = 1000, MinValue = 0)]
        public int Volume { get; set; }

        [Parameter("Buy", DefaultValue = true, Group = "default")]
        public bool Buy { get; set; }

        [Parameter("Sell", DefaultValue = true, Group = "default")]
        public bool Sell { get; set; }
        [Parameter("exponent increase volume", DefaultValue = true, Group = "default")]
        public bool volumeIncrease { get; set; }

        [Parameter("trailing stop loss", DefaultValue = true, Group = "default")]
        public bool trailingStopLoss { get; set; }

        [Parameter("max spread", DefaultValue = 3, MinValue = 0, Group = "default")]
        public double MaxaSpread { get; set; }

        [Parameter("Min AF", DefaultValue = 0.02, MinValue = 0, Group = "default")]
        public double minaf { get; set; }

        [Parameter("Max AF", DefaultValue = 0.2, MinValue = 0, Group = "default")]
        public double maxaf { get; set; }
        #endregion

        int volume;
        double startingBalance;
        DateTime startTime;
        public static Robot myRobot;
        Grid sellGrid, buyGrid;
        double takeProfit;

        protected override void OnStart()
        {
            startingBalance = Account.Balance;
            volume = Volume;
            Positions.Closed += onClosePosition;
            startTime = Server.Time;
            takeProfit = TakeProfit;
            myRobot = this;

            //Grid.RSI = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            //Grid.stoch = Indicators.StochasticOscillator(9, 6, 6, MovingAverageType.Exponential);
            // Put your initialization logic here
            Grid.trailingStopLoss = trailingStopLoss;
            if (Sell)
            {
                sellGrid = new Grid(this, TradeType.Sell, MaxaSpread, PipRadius, volume, takeProfit, minaf, maxaf, riskPips);
            }
            if (Buy)
            {
                buyGrid = new Grid(this, TradeType.Buy, MaxaSpread, PipRadius, volume, takeProfit, minaf, maxaf, riskPips);
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
        double radius, maxSpread, riskPips, startBalance;
        List<Position> gridPositions = new List<Position>();
        TradeType Sell = TradeType.Sell, Buy = TradeType.Buy;
        //public static API.Indicators.RelativeStrengthIndex RSI;
        double takeProfit;
        public static bool trailingStopLoss;
        public API.Indicators.ParabolicSAR _parabolic { get; set; }

        //public static API.Indicators.StochasticOscillator stoch;
        public Grid(Robot robot, TradeType direc, double spreadMax, double rad, int vol, double TP, double minaf, double maxaf, double riskpips)
        {
            myRobot = robot;
            direction = direc;
            volume = vol;
            radius = rad;
            takeProfit = TP;
            maxSpread = spreadMax;
            riskPips = riskpips;
            _parabolic = myRobot.Indicators.ParabolicSAR(minaf, maxaf);
            openTrade(direction, volume, true);

        }

        private void openTrade(TradeType tradeType, int volume, bool firstTrade = false)
        {
            bool sellSignal = _parabolic.Result.LastValue > myRobot.Symbol.Bid;
            // !(RSI.Result.Last(0) > 60);
            bool buySignal = _parabolic.Result.LastValue < myRobot.Symbol.Ask;
            // !(RSI.Result.Last(0) < 40);

            if (tradeType == Sell && !sellSignal && gridPositions.Count == 0)
            {
                return;
            }
            else if (tradeType == Buy && !buySignal && gridPositions.Count == 0)
            {
                return;
            }

            if (myRobot.Symbol.Spread <= maxSpread * myRobot.Symbol.PipSize)
            {
                //myRobot.Print(myRobot.Symbol.Spread * myRobot.Symbol.PipSize);
                if (gridPositions.Count == 0)
                {
                    startBalance = myRobot.Account.Balance;
                }
                TradeResult trade1 = myRobot.ExecuteMarketOrder(tradeType, myRobot.Symbol.Name, volume, "");
                if (trade1.Error != null)
                    myRobot.Print(trade1.Error);
                else
                    gridPositions.Add(trade1.Position);
            }
        }

        private void closeTradeByPosition(Position p, bool settrailingStop = false)
        {
            gridPositions.Remove(p);
            if (settrailingStop && trailingStopLoss)
            {
                TradeResult tr = p.ModifyStopLossPips(-(p.Pips / 2));
                TradeResult tr2 = p.ModifyTrailingStop(true);
                if (tr.Error != null || tr2.Error != null)
                {
                    p.Close();
                }
            }
            else
            {
                p.Close();
            }

        }

        public bool shouldOpenAnotherLevel()
        {
            /// todo: maybe put an indicator to check if i should open this type of trade
            if (gridPositions.Count >= 20000)
                return false;
            if (gridPositions.Count == 0)
                return true;
            Position lastPosition = gridPositions.Last();
            double multiplier = 1;
            //1 + (0.3 * (gridPositions.Count - 1));
            if (lastPosition.Pips < 0 && lastPosition.Pips <= (multiplier * -radius))
            {

                //myRobot.Print("# of trade {0} , multiplier {1}, raius {2}", gridPositions.Count, multiplier, multiplier * radius);
                return true;
            }
            return false;
            // * Math.Floor(gridPositions.Count * 0.0001);
        }

        private bool isProfitGreaterThan(double target)
        {
            double loss = target;
            Position lastPos = gridPositions.Last();
            for (int i = gridPositions.Count - 1; i >= 0; i--)
            {
                loss += gridPositions[i].NetProfit;
                if (gridPositions[i].NetProfit < 0)
                {
                    break;
                }
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
                openTrade(direction, volume, true);
                return;
            }
            else if (gridPositions.Count == 1)
            {
                // if only 1 trade, check if it hits take profit
                Position p = gridPositions[0];
                if (p.Pips >= takeProfit)
                {
                    // take profit
                    closeTradeByPosition(p, true);
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
                if (totalPips >= takeProfit)
                {
                    // take profit
                    closeTradeByPosition(gridPositions[1], true);
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
                if (biggestLoser.Pips <= -riskPips)
                {
                    bool closed = manageRiskByPips(biggestLoser);
                    if (closed)
                    {
                        return;
                    }
                }
                if (shouldOpenAnotherLevel())
                {
                    openTrade(direction, volume);
                }
                else if (biggestLoser.Pips >= takeProfit)
                {
                    closeProfitable();
                }
                else if (isProfitGreaterThan(biggestLoser.NetProfit))
                {
                    balanceOpenAndClose(biggestLoser);
                }

            }
        }

        private void closeProfitable()
        {
            myRobot.Print("how did this scenario happen?");
        }

        private bool manageRiskByPips(Position riskyPos)
        {
            if (startBalance <= myRobot.Account.Balance + riskyPos.NetProfit)
            {
                closeTradeByPosition(riskyPos);
                return true;
            }
            return false;
        }
    }

}
