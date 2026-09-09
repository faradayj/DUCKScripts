/*
name: UltraPulsev4
description: 5-second wall-clock pulse generator for Ultra taunt systems.
tags: ultra, pulse, taunt
*/

//cs_include Scripts/CoreBots.cs

using System;

public class UltraPulsev4
{
    public static int CurrentPulse(DateTime fightStart, int pulseIntervalSec = 5)
        => (int)((DateTime.UtcNow - fightStart).TotalSeconds / pulseIntervalSec);
}
