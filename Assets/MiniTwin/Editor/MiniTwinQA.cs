using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiniTwin.Editor
{
    public static class MiniTwinQA
    {
        private static int checks;
        private static void Check(bool condition,string message)
        {checks++;if(!condition)throw new InvalidOperationException("MiniTwin QA failed: "+message);}

        // [실제 코드에 남길 주석] 기획 경계값·안전 차단·훈련 3종·실제 JSON 저장을 같은 런타임 코드로 검증합니다.
        public static string Run()
        {
            checks=0;var holder=new GameObject("MiniTwin QA isolated core");
            var thresholds=ScriptableObject.CreateInstance<PumpThresholds>();
            var core=holder.AddComponent<MiniTwinCore>();core.thresholds=thresholds;
            try
            {
                Func<float,float,float,float,PumpState> state=(t,v,p,f)=>thresholds.Evaluate(new SensorValues(t,v,p,f),true,0);
                Check(state(62,1.9f,3.8f,50)==PumpState.NORMAL,"nominal");
                Check(state(70,1.9f,3.8f,50)==PumpState.WARNING,"temp 70");
                Check(state(85,1.9f,3.8f,50)==PumpState.WARNING,"temp 85 equality");
                Check(state(85.01f,1.9f,3.8f,50)==PumpState.DANGER,"temp >85");
                Check(state(62,2.8f,3.8f,50)==PumpState.WARNING,"vibration 2.8");
                Check(state(62,7.1f,3.8f,50)==PumpState.WARNING,"vibration 7.1 equality");
                Check(state(62,7.11f,3.8f,50)==PumpState.DANGER,"vibration >7.1");
                Check(state(62,1.9f,2.5f,50)==PumpState.WARNING,"pressure 2.5");
                Check(state(62,1.9f,3,50)==PumpState.NORMAL,"pressure 3");
                Check(state(62,1.9f,2.49f,50)==PumpState.DANGER,"pressure <2.5");
                Check(state(62,1.9f,3.8f,35)==PumpState.WARNING,"flow 35");
                Check(state(62,1.9f,3.8f,45)==PumpState.NORMAL,"flow 45");
                Check(state(62,1.9f,3.8f,34.99f)==PumpState.DANGER,"flow <35");
                Check(state(float.NaN,1.9f,3.8f,50)==PumpState.UNKNOWN,"NaN");
                Check(state(62,1.9f,4.6f,50)==PumpState.UNKNOWN,"undefined high pressure");
                Check(thresholds.Evaluate(null,true,0)==PumpState.UNKNOWN,"missing data");
                Check(thresholds.Evaluate(new SensorValues(62,1,3,50),true,3.1f)==PumpState.UNKNOWN,"stale");
                Check(thresholds.Evaluate(new SensorValues(62,0,0,0),false,0)==PumpState.STOPPED,"stopped precedence");
                core.ResetSimulation();float initial=core.Current.sensors.motorTemp;core.Advance(1);core.ResetSimulation();
                Check(Mathf.Approximately(initial,core.Current.sensors.motorTemp),"seed deterministic reset");
                core.ToggleFreeze();core.Advance(3.1f);Check(core.State==PumpState.UNKNOWN,"actual sensor freeze");core.ToggleFreeze();Check(core.State==PumpState.NORMAL,"sensor recovery");
                for(int i=0;i<75;i++)core.Advance(1);Check(core.History.Count==60,"history capped at 60");
                foreach(FaultType fault in new[]{FaultType.Overheat,FaultType.Vibration,FaultType.LowFlow})
                {
                    core.BeginTraining(fault);Check(core.State==PumpState.DANGER,"fault danger "+fault);
                    Check(core.Act("ppe"),"ppe");core.Inspect(MiniTwinCore.PartFor(fault));
                    Check(core.ChooseCause(fault),"cause");Check(core.Act("stop"),"stop");Check(core.State==PumpState.STOPPED,"stopped");
                    Check(core.Act("isolate"),"isolate");Check(core.Act("repair",MiniTwinCore.PartFor(fault)),"repair");Check(core.Act("restart"),"restart");
                    Check(core.Result.score==100&&core.Result.grade=="S","perfect score "+fault);
                    Check(core.State==PumpState.NORMAL&&!core.TrainingActive,"normal recovery");
                    Check(!string.IsNullOrEmpty(core.LastSavedPath)&&File.Exists(core.LastSavedPath),"JSON file exists");
                    var loaded=JsonUtility.FromJson<TrainingResult>(File.ReadAllText(core.LastSavedPath));
                    Check(loaded.score==100&&loaded.scenarioId==fault.ToString(),"JSON round trip");
                }
                core.BeginTraining(FaultType.Overheat);
                Check(!core.Act("repair","Fan")&&core.Step==0&&core.Running,"unsafe repair blocked");
                Check(!core.Act("repair","Fan")&&core.Step==0,"repeated unsafe action cannot skip");
                core.Act("ppe");core.Inspect("Fan");core.ChooseCause(FaultType.Vibration);core.ChooseCause(FaultType.Overheat);
                core.Act("stop");core.Act("isolate");core.Act("repair","Fan");core.Advance(450);core.Act("restart");
                Check(core.Result.safety==0,"safety not double charged");
                Check(core.Result.accuracy==0&&core.Result.timeScore==0&&core.Result.score==0,"score floors and timeout");
                Check(core.Result.mistakes.Count>=3,"error log retained");
                var summary=new Summary{checks=checks,passed=true,completedAt=DateTimeOffset.Now.ToString("o")};
                Directory.CreateDirectory("QA");File.WriteAllText("QA/core-checks.json",JsonUtility.ToJson(summary,true));
                return "MiniTwin core QA: "+checks+" checks passed.";
            }
            finally{UnityEngine.Object.DestroyImmediate(holder);UnityEngine.Object.DestroyImmediate(thresholds);}
        }
        [Serializable]private sealed class Summary{public int checks;public bool passed;public string completedAt;}
    }
}
