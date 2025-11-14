namespace ZTMZ.PacenoteTool.AC;


public enum AC_FLAGS
{
    ACC_NO_FLAG,
    ACC_BLUE_FLAG,
    ACC_YELLOW_FLAG,
    ACC_BLACK_FLAG,
    ACC_WHITE_FLAG,
    ACC_CHECKERED_FLAG,
    ACC_PENALTY_FLAG,
    ACC_GREEN_FLAG,
    ACC_ORANGE_FLAG
}

public enum AC_SESSION_TYPE
{
	AC_UNKNOWN = -1,
	AC_PRACTICE,
	AC_QUALIFY,
	AC_RACE,
	AC_HOTLAP,
	AC_TIME_ATTACK,
	AC_DRIFT,
	AC_DRAG
}

public enum AC_STATUS
{
    AC_OFF,
    AC_REPLAY,
    AC_LIVE,
    AC_PAUSE
}

public enum ACC_PENALTY_TYPE
{
    ACC_None,
    ACC_DriveThrough_Cutting,
    ACC_StopAndGo_10_Cutting,
    ACC_StopAndGo_20_Cutting,
    ACC_StopAndGo_30_Cutting,
    ACC_Disqualified_Cutting,
    ACC_RemoveBestLaptime_Cutting,
    ACC_DriveThrough_PitSpeeding,
    ACC_StopAndGo_10_PitSpeeding,
    ACC_StopAndGo_20_PitSpeeding,
    ACC_StopAndGo_30_PitSpeeding,
    ACC_Disqualified_PitSpeeding,
    ACC_RemoveBestLaptime_PitSpeeding,
    ACC_Disqualified_IgnoredMandatoryPit,
    ACC_PostRaceTime,
    ACC_Disqualified_Trolling,
    ACC_Disqualified_PitEntry,
    ACC_Disqualified_PitExit,
    ACC_Disqualified_WrongWay,
    ACC_DriveThrough_IgnoredDriverStint,
    ACC_Disqualified_IgnoredDriverStint,
    ACC_Disqualified_ExceededDriverStintLimit
}

public enum ACC_RAIN_INTENSITY
{
    ACC_NO_RAIN,
    ACC_DRIZZLE,
    ACC_LIGHT_RAIN,
    ACC_MEDIUM_RAIN,
    ACC_HEAVY_RAIN,
    ACC_THUNDERSTORM
}

public enum ACC_TRACK_GRIP_STATUS
{
	ACC_GREEN,
	ACC_FAST,
	ACC_OPTIMUM,
	ACC_GREASY,
	ACC_DAMP,
	ACC_WET,
	ACC_FLOODED
}

