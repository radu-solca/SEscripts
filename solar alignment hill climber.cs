using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        private SolarArray solarArray;
        private readonly Rotor rotor;
        private readonly Hinge hinge;
        private readonly List<IMySolarPanel> solarPanels = new List<IMySolarPanel>();

        private Logger logger;

        public Program()
        {
            logger = new Logger();

            rotor = new Rotor((IMyMotorStator)(GridTerminalSystem.GetBlockWithName("Rotor")), logger);
            hinge = new Hinge((IMyMotorStator)(GridTerminalSystem.GetBlockWithName("Hinge")), logger);
            GridTerminalSystem.GetBlocksOfType<IMySolarPanel>(solarPanels);

            solarArray = new SolarArray(rotor, hinge, solarPanels, logger);


            currentStateMachine = solarArray.ExploreNeighboursOverTime();
            //currentStateMachine = solarArray.OrientToOverTime(new Orientation(10, 10)).Then(solarArray.OrientToOverTime(new Orientation(0, 0)));
            //currentStateMachine = rotor.MoveToAngleOverTime(90);
            Runtime.UpdateFrequency |= UpdateFrequency.Once;
        }
        
        public void Main(string argument, UpdateType updateSource)
        {
            logger.Iteration++;

            if ((updateSource & UpdateType.Once) == UpdateType.Once)
            {
                RunNextStepInStateMachine();
            }

            Echo(logger.GetDebugDisplay());
        }

        private IEnumerator currentStateMachine = null;
        public void RunNextStepInStateMachine()
        {
            if (currentStateMachine == null)
            {
                return;
            }

            bool hasMoreSteps = currentStateMachine.MoveNext();
            if (hasMoreSteps)
            {
                Runtime.UpdateFrequency |= UpdateFrequency.Once;
            }
            else
            {
                currentStateMachine = null;
            }
        }

        public sealed class SolarArray
        {
            private readonly Logger logger;

            private readonly Motor xAxisMotor;
            private readonly Motor yAxisMotor;
            private readonly IEnumerable<IMySolarPanel> solarPanels;

            public SolarArray(Motor xAxisMotor, Motor yAxisMotor, IEnumerable<IMySolarPanel> solarPanels, Logger logger)
            {
                this.xAxisMotor = xAxisMotor;
                this.yAxisMotor = yAxisMotor;
                this.solarPanels = solarPanels;
                this.logger = logger;
            }

            //TODO: not really class state, more like state of the exploration functions.. best way to handle?
            //private float referenceOutputInMW = 0;
            //private Orientation referenceOutputOrientation = new Orientation(0, 0);

            public IEnumerator ExploreNeighboursOverTime()
            {
                var referenceOutputInMW = GetCurrentOutputInMW();
                var referenceOutputOrientation = GetCurrentOrientation();

                var neighborOrientations = referenceOutputOrientation.GetNeighbourOrientations(15);

                var exploreCoroutine = CoroutineExtensions.Empty();
                foreach (var neighborOrientation in neighborOrientations)
                {
                    exploreCoroutine = exploreCoroutine
                        .Then(MoveToOrientationOverTime(neighborOrientation))
                        .Then(CoroutineExtensions.FromAction(() => {
                            var currentOutputInMW = GetCurrentOutputInMW();
                            if (currentOutputInMW > referenceOutputInMW)
                            {
                                referenceOutputInMW = currentOutputInMW;
                                referenceOutputOrientation = GetCurrentOrientation();
                            }
                        }));
                }

                exploreCoroutine = exploreCoroutine.Then(CoroutineExtensions.FromCoroutineFactory(() => MoveToOrientationOverTime(referenceOutputOrientation)));

                return exploreCoroutine;
            }

            //private void UpdateReferenceOutput()
            //{
            //    var currentOutputInMW = GetCurrentOutputInMW();

            //    logger.Status = $"Comparing... \ncurrent output: {currentOutputInMW};\nprevious best: {referenceOutputInMW};\nprevious best orientation:{referenceOutputOrientation}";

            //    if (currentOutputInMW > referenceOutputInMW)
            //    {
            //        referenceOutputInMW = currentOutputInMW;
            //        referenceOutputOrientation = GetCurrentOrientation();
            //        logger.Status += $"\nNew optimum orientation: {referenceOutputOrientation}, with output: {referenceOutputInMW}MW";
            //    }
            //}

            private float GetCurrentOutputInMW()
            {
                var currentOutputInMW = 0f;
                foreach (var solarPanel in solarPanels)
                {
                    currentOutputInMW += solarPanel.MaxOutput;
                }
                return currentOutputInMW;
            }

            private Orientation GetCurrentOrientation()
            {
                return new Orientation(xAxisMotor.GetAproximateAngleInDegrees(), yAxisMotor.GetAproximateAngleInDegrees());
            }

            //public IEnumerator MoveToOrientationOverTime(Func<Orientation> orientationGetter)
            //{
            //    logger.Status = $"Orienting to {{idk yet ;)}}...";

            //    var xAxisMoveTask = xAxisMotor.MoveToAngleOverTime(() => orientationGetter().AngleOnXAxisInDegrees);
            //    var yAxisMoveTask = yAxisMotor.MoveToAngleOverTime(() => orientationGetter().AngleOnYAxisInDegrees);

            //    return xAxisMoveTask.InParallelWith(yAxisMoveTask);
            //}

            public IEnumerator MoveToOrientationOverTime(Orientation orientation)
            {
                logger.Status = $"Orienting to {orientation}...";

                var xAxisMoveTask = xAxisMotor.MoveToAngleOverTime(orientation.AngleOnXAxisInDegrees);
                var yAxisMoveTask = yAxisMotor.MoveToAngleOverTime(orientation.AngleOnYAxisInDegrees);

                return xAxisMoveTask.InParallelWith(yAxisMoveTask);
            }
        }


        public sealed class Orientation : IEquatable<Orientation>
        {
            public int AngleOnXAxisInDegrees { get; }
            public int AngleOnYAxisInDegrees { get; }

            public Orientation(int angleOnXAxisInDegrees, int angleOnYAxisInDegrees)
            {
                AngleOnXAxisInDegrees = angleOnXAxisInDegrees;
                AngleOnYAxisInDegrees = angleOnYAxisInDegrees;
            }

            public IEnumerable<Orientation> GetNeighbourOrientations(int distanceInDegrees)
            {
                yield return new Orientation(
                    AngleOnXAxisInDegrees + distanceInDegrees,
                    AngleOnYAxisInDegrees
                );

                yield return new Orientation(
                    AngleOnXAxisInDegrees,
                    AngleOnYAxisInDegrees + distanceInDegrees
                );

                yield return new Orientation(
                    AngleOnXAxisInDegrees - distanceInDegrees,
                    AngleOnYAxisInDegrees
                );

                yield return new Orientation(
                    AngleOnXAxisInDegrees,
                    AngleOnYAxisInDegrees - distanceInDegrees
                );
            }

            public override bool Equals(object obj) => Equals(obj as Orientation);

            public bool Equals(Orientation other)
            {
                if (other == null) return false;

                return AngleOnXAxisInDegrees == other.AngleOnXAxisInDegrees && AngleOnYAxisInDegrees == other.AngleOnYAxisInDegrees;
            }

            public override int GetHashCode()
            {
                int hashCode = -896191723;
                hashCode = hashCode * -1521134295 + AngleOnXAxisInDegrees.GetHashCode();
                hashCode = hashCode * -1521134295 + AngleOnYAxisInDegrees.GetHashCode();
                return hashCode;
            }

            public static bool operator ==(Orientation lhs, Orientation rhs)
            {
                if (lhs == null)
                {
                    if (rhs == null)
                    {
                        return true;
                    }
                    return false;
                }
                return lhs.Equals(rhs);
            }

            public static bool operator !=(Orientation lhs, Orientation rhs) => !(lhs == rhs);

            public override string ToString()
            {
                return $"X:{AngleOnXAxisInDegrees}°, Y:{AngleOnYAxisInDegrees}°";
            }
        }

        public abstract class Motor
        {
            protected readonly IMyMotorStator motor;
            protected readonly Logger logger;
            public float VelocityInRPM { get; set; } = 1;

            public Motor(IMyMotorStator motor, Logger logger)
            {
                this.motor = motor;
                this.logger = logger;
            }

            //public IEnumerator MoveToAngleOverTime(Func<int> targetAngleInDegreesGetter)
            //{
            //    var targetAngleInDegrees = targetAngleInDegreesGetter();

            //    var coroutine = MoveToAngleOverTime(targetAngleInDegrees);


            //    //var actualTargetInDegrees = GetClosestAngleWithinBoundsToAngle(targetAngleInDegrees);
            //    //var distanceToTargetInDegrees = GetShortestDistanceInDegreesToAngle(actualTargetInDegrees);

            //    //while (distanceToTargetInDegrees != 0)
            //    //{
            //    //    actualTargetInDegrees = GetClosestAngleWithinBoundsToAngle(targetAngleInDegrees);
            //    //    distanceToTargetInDegrees = GetShortestDistanceInDegreesToAngle(actualTargetInDegrees);

            //    //    motor.TargetVelocityRPM = VelocityInRPM * Math.Sign(distanceToTargetInDegrees);
            //    //    yield return null;
            //    //}

            //    //motor.TargetVelocityRPM = 0;
            //    //yield break;

            //    //TODO: duplication NOOOOOOOOOOOOOO
            //}

            public IEnumerator MoveToAngleOverTime(int targetAngleInDegrees)
            {
                var actualTargetInDegrees = GetClosestAngleWithinBoundsToAngle(targetAngleInDegrees);
                var distanceToTargetInDegrees = GetShortestDistanceInDegreesToAngle(actualTargetInDegrees);

                while (distanceToTargetInDegrees != 0)
                {
                    actualTargetInDegrees = GetClosestAngleWithinBoundsToAngle(targetAngleInDegrees);
                    distanceToTargetInDegrees = GetShortestDistanceInDegreesToAngle(actualTargetInDegrees);

                    motor.TargetVelocityRPM = VelocityInRPM * Math.Sign(distanceToTargetInDegrees);
                    yield return null;
                }

                motor.TargetVelocityRPM = 0;
                yield break;
            }

            public int GetAproximateAngleInDegrees()
            {
                return (int)Math.Round(motor.Angle * 57.2958f % 360);
            }

            protected abstract int GetClosestAngleWithinBoundsToAngle(int angleInDegrees);

            protected abstract int GetShortestDistanceInDegreesToAngle(int angleInDegrees);
        }
        public sealed class Rotor : Motor
        {
            public Rotor(IMyMotorStator motor, Logger logger) : base(motor, logger) { }

            protected override int GetClosestAngleWithinBoundsToAngle(int angleInDegrees)
            {
                return angleInDegrees % 360;
            }

            protected override int GetShortestDistanceInDegreesToAngle(int angleInDegrees)
            {
                var diff = angleInDegrees - GetAproximateAngleInDegrees();

                if (diff > 180) diff -= 360;
                if (diff < -180) diff += 360;

                return diff;
            }
        }
        public sealed class Hinge : Motor
        {
            public Hinge(IMyMotorStator motor, Logger logger) : base(motor, logger) { }

            protected override int GetClosestAngleWithinBoundsToAngle(int angleInDegrees)
            {
                if (angleInDegrees > 90) return 90;
                if (angleInDegrees < -90) return -90;
                return angleInDegrees;
            }

            protected override int GetShortestDistanceInDegreesToAngle(int angleInDegrees)
            {
                return angleInDegrees - GetAproximateAngleInDegrees();
            }
        }

        public sealed class Logger
        {
            public int Iteration { get; set; }
            public string Status { get; set; }

            public string GetDebugDisplay()
            {
                return $"Iteration: {Iteration}\n" +
                    $"Current status: {Status}\n";
            }
        }
    }

    public static class CoroutineExtensions
    {
        public static IEnumerator InParallelWith(this IEnumerator self, IEnumerator other)
        {
            while (true)
            {
                var selfHasMoreSteps = self.MoveNext();
                var otherHasMoreSteps = other.MoveNext();

                if (!selfHasMoreSteps && !otherHasMoreSteps)
                {
                    break;
                }

                yield return null;
            }

            yield break;
        }

        public static IEnumerator Then(this IEnumerator self, IEnumerator other)
        {
            while (self.MoveNext())
            {
                yield return null;
            }

            while (other.MoveNext())
            {
                yield return null;
            }

            yield break;
        }

        public static IEnumerator Empty()
        {
            yield break;
        }

        public static IEnumerator FromAction(Action action)
        {
            action();
            yield break;
        }

        public static IEnumerator FromCoroutineFactory(Func<IEnumerator> factory)
        {
            var coroutine = factory();
            while (coroutine.MoveNext())
            {
                yield return true;
            }
            yield break;
        }
    }
}
