
using System.Linq;
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool.RBR;

// what if I remove all the LayoutKind.Sequential, Pack = 1
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct RBRUdpData
{
	public uint totalSteps;

	public Stage stage;

	public Control control;

	public Car car;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Stage
{
	public int index;

	public float progress;

	public float raceTime;

	public float driveLineLocation;

	public float distanceToEnd;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Control
{
	public float steering;

	public float throttle;

	public float brake;

	public float handbrake;

	public float clutch;

	public int gear;

	public float footbrakePressure;

	public float handbrakePressure;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Car
{
	public int index;

	public float speed;

	public float positionX;

	public float positionY;

	public float positionZ;

	public float roll;

	public float pitch;

	public float yaw;

	public Motion velocities;

	public Motion accelerations;

	public Engine engine;

	public Suspension suspensionLF;

	public Suspension suspensionRF;

	public Suspension suspensionLB;

	public Suspension suspensionRB;

	public Suspension[] GetSuspensions()
	{
		return new Suspension[4] { suspensionLF, suspensionRF, suspensionLB, suspensionRB };
	}
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Motion
{
	public float surge;

	public float sway;

	public float heave;

	public float roll;

	public float pitch;

	public float yaw;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Engine
{
	public float rpm;

	public float radiatorCoolantTemperature;

	public float engineCoolantTemperature;

	public float engineTemperature;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Suspension
{
	public float springDeflection;

	public float rollbarForce;

	public float springForce;

	public float damperForce;

	public float strutForce;

	public int helperSpringIsActive;

	public Damper damper;

	public Wheel wheel;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Damper
{
	public float damage;

	public float pistonVelocity;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Wheel
{
	public BrakeDisk brakeDisk;

	public Tire tire;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BrakeDisk
{
	public float layerTemperature;

	public float temperature;

	public float wear;
}


[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Tire
{
	public float pressure;

	public float temperature;

	public float carcassTemperature;

	public float treadTemperature;

	public uint currentSegment;

	public TireSegment segment1;

	public TireSegment segment2;

	public TireSegment segment3;

	public TireSegment segment4;

	public TireSegment segment5;

	public TireSegment segment6;

	public TireSegment segment7;

	public TireSegment segment8;

	public float GetWear()
	{
		return new TireSegment[8] { segment1, segment2, segment3, segment4, segment5, segment6, segment7, segment8 }.Min((TireSegment i) => 1f - i.wear) * 100f;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TireSegment
{
	public float temperature;

	public float wear;
}

