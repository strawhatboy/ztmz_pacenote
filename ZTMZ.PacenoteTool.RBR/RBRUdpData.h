// vim: set ts=4 :
///////////////////////////////////////////////////////////
//  rbr.telemetry.data.TelemetryData.h
//  Implementation of the Class TireSegment
//  Created on:      28-Dez-2019 07:49:08
//  Original author: Guenter Schlupf
///////////////////////////////////////////////////////////
#pragma once

#pragma pack (push, 1)

namespace rbr { namespace telemetry { namespace data { 

struct TireSegment
{

public:
    float temperature_;
    float wear_;
};



struct Tire
{

public:
    float pressure_;
    float temperature_;
    float carcassTemperature_;
    float treadTemperature_;
    unsigned int currentSegment_;
    TireSegment segment1_;
    TireSegment segment2_;
    TireSegment segment3_;
    TireSegment segment4_;
    TireSegment segment5_;
    TireSegment segment6_;
    TireSegment segment7_;
    TireSegment segment8_;

};



struct BrakeDisk
{

public:
    float layerTemperature_;
    float temperature_;
    float wear_;

};



struct Wheel
{

public:
    BrakeDisk brakeDisk_;
    Tire tire_;

};



struct Damper
{

public:
    float damage_;
    float pistonVelocity_;

};



struct Suspension
{

public:
    float springDeflection_;
    float rollbarForce_;
    float springForce_;
    float damperForce_;
    float strutForce_;
    int helperSpringIsActive_;
    Damper damper_;
    Wheel wheel_;

};



struct Engine
{

public:
    float rpm_;
    float radiatorCoolantTemperature_;
    float engineCoolantTemperature_;
    float engineTemperature_;

};



struct Motion
{

public:
    /// <summary>
    /// Forward/backward.
    /// </summary>
    float surge_;
    /// <summary>
    /// Left/right.
    /// </summary>
    float sway_;
    /// <summary>
    /// Up/down.
    /// </summary>
    float heave_;
    /// <summary>
    /// Rotation about longitudinal axis.
    /// </summary>
    float roll_;
    /// <summary>
    /// Rotation about transverse axis.
    /// </summary>
    float pitch_;
    /// <summary>
    /// Rotation about normal axis.
    /// </summary>
    float yaw_;

};



struct Car
{

public:
    int index_;
    /// <summary>
    /// Speed of the car in kph or mph.
    /// </summary>
    float speed_;
    float positionX_;
    float positionY_;
    float positionZ_;
    float roll_;
    float pitch_;
    float yaw_;
    Motion velocities_;
    Motion accelerations_;
    Engine engine_;
    /// <summary>
    /// Suspension data: LF, RF, LB, RB.
    /// </summary>
    Suspension suspensionLF_;
    /// <summary>
    /// Suspension data: LF, RF, LB, RB.
    /// </summary>
    Suspension suspensionRF_;
    /// <summary>
    /// Suspension data: LF, RF, LB, RB.
    /// </summary>
    Suspension suspensionLB_;
    /// <summary>
    /// Suspension data: LF, RF, LB, RB.
    /// </summary>
    Suspension suspensionRB_;

};



struct Control
{

public:
    float steering_;
    float throttle_;
    float brake_;
    float handbrake_;
    float clutch_;
    int gear_;
    float footbrakePressure_;
    float handbrakePressure_;

};



struct Stage
{

public:
    int index_;
    /// <summary>
    /// The position on the driveline.
    /// </summary>
    float progress_;
    /// <summary>
    /// The total race time.
    /// </summary>
    float raceTime_;
    float driveLineLocation_;
    float distanceToEnd_;

};



struct TelemetryData
{

public:
    unsigned int totalSteps_;
    Stage stage_;
    Control control_;
    Car car_;

};

}}}

#pragma pack (pop)

