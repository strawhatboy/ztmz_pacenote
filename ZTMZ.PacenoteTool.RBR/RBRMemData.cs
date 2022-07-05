namespace ZTMZ.PacenoteTool.RBR;
public struct RBRMemData
{
	public float TrackLength { get; set; }

	public float SpeedKMH { get; set; }

	public float EngineRPM { get; set; }

	public float WaterTemperatureCelsius { get; set; }

	public float TurboPressureBar { get; set; }

	public float TravelledDistance { get; set; }

	public float DistanceFromStart { get; set; }

	public float DistanceToFinish { get; set; }

	public float RaceTime { get; set; }

	public bool WrongWay { get; set; }

	public int GearId { get; set; }

	public float Steering { get; set; }

	public float Throttle { get; set; }

	public float Brake { get; set; }

	public float Handbrake { get; set; }

	public float Clutch { get; set; }

	public byte GameStateId { get; set; }

	public float X { get; set; }

	public float Y { get; set; }

	public float Z { get; set; }

	public float SessionTime { get; set; }

	public float XSpin { get; set; }

	public float YSpin { get; set; }

	public float ZSpin { get; set; }

	public float XSpeed { get; set; }

	public float YSpeed { get; set; }

	public float ZSpeed { get; set; }

	public float CurrentStagePosition { get; set; }

	public string CarModel { get; set; }

	public string Track { get; set; }

	public int CarModelId { get; set; }

	public int TrackId { get; set; }

	public int DamageId { get; set; }

	public string Damage { get; set; }

	public int WeatherId { get; set; }

	public string Weather { get; set; }

	public int TransmissionId { get; set; }

	public string Transmission { get; set; }

	public bool IsRunning { get; set; }

	public string Gear { get; set; }

	public bool Split1Done { get; set; }

	public bool Split2Done { get; set; }

	public float Split1Time { get; set; }

	public float Split2Time { get; set; }

	public bool RaceEnded { get; set; }

	public double GroundSpeed { get; set; }

	public bool IsRendering { get; set; }

	public double Roll { get; set; }

	public double Pitch { get; set; }

	public double Yaw { get; set; }

	public bool FalseStart { get; set; }

	public float StageStartCountdown { get; set; }

	public float GuessedLength { get; set; }


	public double CurrentGearUpshiftRpm { get; set; }

	public float FFBValue { get; set; }
}
