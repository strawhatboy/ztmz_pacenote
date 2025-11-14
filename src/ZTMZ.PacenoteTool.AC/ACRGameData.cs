// ACR game use MMF data structure

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool.AC;

[Serializable]
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ACRVector3
{
    public float XCoord;

    public float YCoord;

    public float ZCoord;

    public IEnumerable<float> Coordinates()
    {
        yield return XCoord;
        yield return YCoord;
        yield return ZCoord;
    }
}

public struct Vector3Position
{
	public float XPos;

	public float YPos;

	public float ZPos;
}

[Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct ACRGraphicsData
{
    public int PacketIdentifier;

    public AC_STATUS CurrentStatus;

    public AC_SESSION_TYPE SessionType;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string CurrentLapTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string LastLapTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string BestLapTime;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string SplitTime;

    public int LapsCompleted;

    public int CurrentPosition;

    public int CurrentTimeMillis;

    public int LastTimeMillis;

    public int BestTimeMillis;

    public float SessionRemainingTime;

    public float TotalDistanceTraveled;

    public int InPitBox;

    public int ActiveSectorIndex;

    public int PreviousSectorTime;

    public int TotalLapsInSession;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TyreCompoundName;

    public float ReplaySpeedMultiplier;

    public float NormalizedCarPos;

    public int ActiveCarsCount;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public ACRVector3[] OpponentCarPositions;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 60)]
    public int[] OpponentCarIdentifiers;

    public int PlayerCarIdentifier;

    public float AccumulatedPenaltyTime;

    public AC_FLAGS CurrentFlag;

    public ACC_PENALTY_TYPE PenaltyType;

    public int IdealLineEnabled;

    public int InPitLaneArea;

    public float TrackSurfaceGrip;

    public int MandatoryPitCompleted;

    public float WindVelocity;

    public float WindAngle;

    public int SetupMenuVisible;

    public int PrimaryDisplayIndex;

    public int SecondaryDisplayIndex;

    public int TractionControlLevel;

    public int TractionControlCutLevel;

    public int EngineMapSetting;

    public int ABSLevel;

    public float FuelPerLap;

    public int RainLightsActive;

    public int FlashingLightsActive;

    public int HeadlightsStage;

    public float ExhaustTemp;

    public int WiperLevel;

    public int DriverStintRemainingTotal;

    public int DriverStintRemainingCurrent;

    public int RainTyresEquipped;

    public int ActiveSessionIndex;

    public float FuelConsumed;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string DeltaLapTimeString;

    public int DeltaLapTimeMillis;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
    public string EstimatedLapTimeString;

    public int EstimatedLapTimeMillis;

    public int DeltaIsPositive;

    public int SplitTimeMillis;

    public int LapIsValid;

    public float EstimatedFuelLaps;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
    public string TrackStatusString;

    public int MandatoryPitsMissing;

    public float SessionClock;

    public int LeftDirectionLights;

    public int RightDirectionLights;

    public int YellowFlagGlobal;

    public int YellowFlagSector1;

    public int YellowFlagSector2;

    public int YellowFlagSector3;

    public int WhiteFlagGlobal;

    public int GreenFlagGlobal;

    public int ChequeredFlagGlobal;

    public int RedFlagGlobal;

    public int MFDTyreSetSelection;

    public float MFDFuelToAddAmount;

    public float MFDTyrePressureFrontLeft;

    public float MFDTyrePressureFrontRight;

    public float MFDTyrePressureRearLeft;

    public float MFDTyrePressureRearRight;

    public ACC_TRACK_GRIP_STATUS TrackGripCondition;

    public ACC_RAIN_INTENSITY CurrentRainIntensity;

    public ACC_RAIN_INTENSITY RainIntensity10Min;

    public ACC_RAIN_INTENSITY RainIntensity30Min;

    public int ActiveTyreSet;

    public int StrategyTyreSetTarget;

    public int TimeGapAhead;

    public int TimeGapBehind;

    internal bool IsValidLap()
    {
        return LapIsValid > 0;
    }
}

[Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct ACRPhysicsData
{
    public int PacketIdentifier;

    public float ThrottleInput;

    public float BrakeInput;

    public float FuelLevel;

    public int CurrentGear;

    public int EngineRPM;

    public float SteeringAngle;

    public float SpeedKilometersPerHour;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] VelocityVector;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] AccelerationG;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelSlipRatio;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelLoadForce;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyrePressureValues;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] WheelAngularVelocity;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreWearLevel;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreDirtLevel;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreCoreTemp;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] CamberAngleRadians;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionTravelDistance;

    public float DRSLevel;

    public float TractionControlLevel;

    public float CarHeading;

    public float CarPitch;

    public float CarRoll;

    public float CenterOfGravityHeight;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public float[] VehicleDamage;

    public int TyresOutOfTrack;

    public float PitLimiterActive;

    public float ABSLevel;

    public float KERSCharge;

    public float KERSInput;

    public int AutoShifterEnabled;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public float[] RideHeightValues;

    public float TurboBoostPressure;

    public float BallastWeight;

    public float AirDensityValue;

    public float AmbientTemperature;

    public float RoadSurfaceTemp;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalAngularVelocityVector;

    public float ForceFeedbackValue;

    public float PerformanceMeterValue;

    public int EngineBrakeSetting;

    public int ERSRecoveryLevel;

    public int ERSPowerLevel;

    public int ERSHeatCharging;

    public int ERSIsCharging;

    public float KERSCurrentKilojoules;

    public int DRSAvailable;

    public int DRSEnabled;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakeTemperatures;

    public float ClutchPosition;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempInner;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempMiddle;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTempOuter;

    public int AIControlled;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Vector3Position[] TyreContactPoints;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Vector3Position[] TyreContactNormals;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public Vector3Position[] TyreContactHeadings;

    public float BrakeBiasValue;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public float[] LocalVelocityVector;

    public int PushToPassActivations;

    public int PushToPassStatus;

    public float MaximumRPM;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreMomentZ;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreForceX;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreForceY;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreSlipRatioValue;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreSlipAngleValue;

    public int TractionControlActive;

    public int ABSActive;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] SuspensionDamageLevel;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] TyreTemperatureAverage;

    public float WaterTemperature;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakePressureValues;

    public int FrontBrakeCompound;

    public int RearBrakeCompound;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakePadLife;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
    public float[] BrakeDiscLife;

    public int IgnitionActive;

    public int StarterMotorActive;

    public int EngineRunning;

    public float KerbVibrationLevel;

    public float SlipVibrationLevel;

    public float GForceVibrationLevel;

    public float ABSVibrationLevel;
}

[Serializable]
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
public struct ACRStaticInfo
{
	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
	public string SharedMemoryVersion;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 15)]
	public string AssettoCorsaVersion;

	public int TotalSessions;

	public int TotalCarsInSession;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string VehicleModel;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string TrackName;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string DriverFirstName;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string DriverLastName;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string DriverNickname;

	public int TrackSectorCount;

	public float EngineTorqueMax;

	public float EnginePowerMax;

	public int EngineRPMMax;

	public float FuelCapacityMax;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
	public float[] SuspensionMaxTravelDistance;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
	public float[] TyreRadiusValues;

	public float TurboBoostMax;

	public float DeprecatedField1;

	public float DeprecatedField2;

	public int PenaltiesActive;

	public float AssistFuelConsumptionRate;

	public float AssistTyreWearRate;

	public float AssistMechanicalDamageRate;

	public int AssistTyreBlanketsAllowed;

	public float AssistStabilityControl;

	public int AssistAutoClutchEnabled;

	public int AssistAutoBlipEnabled;

	public int VehicleHasDRS;

	public int VehicleHasERS;

	public int VehicleHasKERS;

	public float KERSMaxKilojoules;

	public int EngineBrakeSettingsAvailable;

	public int ERSPowerControllerCount;

	public float TrackLengthMeters;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string TrackLayoutConfiguration;

	public float ERSMaxKilojoules;

	public int SessionIsTimedRace;

	public int SessionHasExtraLap;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string VehicleSkinName;

	public int GridReversedPositions;

	public int PitWindowOpeningLap;

	public int PitWindowClosingLap;

	public int SessionIsOnline;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string DryTyreCompoundName;

	[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 33)]
	public string WetTyreCompoundName;
}

public class ACRGameRawData
{
    public ACRGraphicsData GraphicsData;
    public ACRPhysicsData PhysicsData;
    public ACRStaticInfo StaticInfo;
}


