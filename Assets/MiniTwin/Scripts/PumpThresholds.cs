using UnityEngine;
namespace MiniTwin
{
    [CreateAssetMenu(menuName = "MiniTwin/Pump thresholds")]
    public sealed class PumpThresholds : ScriptableObject
    {
        // [실제 코드에 남길 주석] 제조사 안전 기준이 아닌 기획서의 가상 센서 판정값입니다.
        public float tempMin = 40, tempWarning = 70, tempDanger = 85;
        public float vibrationWarning = 2.8f, vibrationDanger = 7.1f;
        public float pressureDanger = 2.5f, pressureNormal = 3, pressureMax = 4.5f;
        public float flowDanger = 35, flowNormal = 45, flowMax = 55;
        public float staleSeconds = 3;

        public PumpState Evaluate(SensorValues s, bool running, float age)
        {
            if (s == null || age > staleSeconds || !Valid(s.motorTemp) || !Valid(s.vibration)
                || !Valid(s.pressure) || !Valid(s.flow) || s.motorTemp < tempMin
                || s.pressure > pressureMax || s.flow > flowMax) return PumpState.UNKNOWN;
            if (!running) return PumpState.STOPPED;
            if (s.motorTemp > tempDanger || s.vibration > vibrationDanger
                || s.pressure < pressureDanger || s.flow < flowDanger) return PumpState.DANGER;
            if (s.motorTemp >= tempWarning || s.vibration >= vibrationWarning
                || s.pressure < pressureNormal || s.flow < flowNormal) return PumpState.WARNING;
            return PumpState.NORMAL;
        }
        private bool Valid(float v) { return !float.IsNaN(v) && !float.IsInfinity(v) && v >= 0; }
    }

}
