using System;
using System.Runtime.InteropServices;

namespace ZTMZ.PacenoteTool;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WRCDataSessionStart {
    public float vehicle_position_z;
    public double stage_length;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WRCDataSessionPauseResumeEnd {
    public float stage_current_time;
    public double stage_current_distance;
}


[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct WRCDataStructure {
    public UInt64 packet_uid;
    public float game_total_time;   //float32
    public float game_delta_time;
    public UInt64 game_frame_count;
    public float shiftlights_fraction;
    public float shiftlights_rpm_start;
    public float shiftlights_rpm_end;

    [MarshalAs(UnmanagedType.U1)]
    public bool shiftlights_rpm_valid;  //bool: 1 bytes
    public byte vehicle_gear_index; //uint8
    public byte vehicle_gear_index_neutral;
    public byte vehicle_gear_index_reverse;
    public byte vehicle_gear_maximum;
    public float vehicle_speed;
    public float vehicle_transmission_speed;
    public float vehicle_position_x;
    public float vehicle_position_y;
    public float vehicle_position_z;
    public float vehicle_velocity_x;
    public float vehicle_velocity_y;
    public float vehicle_velocity_z;
    public float vehicle_acceleration_x;
    public float vehicle_acceleration_y;
    public float vehicle_acceleration_z;
    public float vehicle_left_direction_x;
    public float vehicle_left_direction_y;
    public float vehicle_left_direction_z;
    public float vehicle_forward_direction_x;
    public float vehicle_forward_direction_y;
    public float vehicle_forward_direction_z;
    public float vehicle_up_direction_x;
    public float vehicle_up_direction_y;
    public float vehicle_up_direction_z;
    public float vehicle_hub_position_bl;
    public float vehicle_hub_position_br;
    public float vehicle_hub_position_fl;
    public float vehicle_hub_position_fr;
    public float vehicle_hub_velocity_bl;
    public float vehicle_hub_velocity_br;
    public float vehicle_hub_velocity_fl;
    public float vehicle_hub_velocity_fr;
    public float vehicle_cp_forward_speed_bl;
    public float vehicle_cp_forward_speed_br;
    public float vehicle_cp_forward_speed_fl;
    public float vehicle_cp_forward_speed_fr;
    public float vehicle_brake_temperature_bl;
    public float vehicle_brake_temperature_br;
    public float vehicle_brake_temperature_fl;
    public float vehicle_brake_temperature_fr;
    public float vehicle_engine_rpm_max;
    public float vehicle_engine_rpm_idle;
    public float vehicle_engine_rpm_current;
    public float vehicle_throttle;
    public float vehicle_brake;
    public float vehicle_clutch;
    public float vehicle_steering;
    public float vehicle_handbrake;
    public float stage_current_time;
    public double stage_current_distance;
    public double stage_length;
    public UInt16 vehicle_id;
    public UInt16 vehicle_class_id;
    public UInt16 vehicle_model_id;
    public UInt16 location_id;
    public UInt16 route_id;
    public float stage_previous_split_time;
    public float stage_result_time;
    public float stage_result_time_penalty;
    public byte stage_result_status;
    public float stage_progress;
    public byte vehicle_tyre_state_bl;
    public byte vehicle_tyre_state_br;
    public byte vehicle_tyre_state_fl;
    public byte vehicle_tyre_state_fr;
    [MarshalAs(UnmanagedType.U1)]
    public bool stage_shakedown;
    public byte game_mode;
    [MarshalAs(UnmanagedType.U1)]
    public bool vehicle_cluster_abs;
}

public enum WRCStageResultStatus {
    NOT_FINISHED = 0,
    FINISHED = 1,
    TIME_OUT_STAGE = 2,
    TERMINALLY_DAMAGED = 3,
    RETIRED = 4,
    DISQUALIFIED = 5,
    UNKNOWN = 6
}


