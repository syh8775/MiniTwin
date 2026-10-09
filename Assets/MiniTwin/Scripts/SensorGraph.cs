using UnityEngine;
using UnityEngine.UI;

namespace MiniTwin
{
    // [실제 코드에 남길 주석] 저장된 실제 센서 스냅샷을 UI 메쉬로 그립니다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SensorGraph : MaskableGraphic
    {
        public MiniTwinCore core;
        public int sensor;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (core == null || core.History.Count < 2) return;
            Rect rect = rectTransform.rect;
            float maximum = sensor == 0 ? 110 : sensor == 1 ? 12 : sensor == 2 ? 6 : 65;
            for (int i = 1; i < core.History.Count; i++)
            {
                Vector2 a = new Vector2(rect.xMin + (59f-(core.History.Count-1-i+1)) / 59f * rect.width, rect.yMin + Mathf.Clamp01(Value(core.History[i - 1].sensors) / maximum) * rect.height);
                Vector2 b = new Vector2(rect.xMin + (59f-(core.History.Count-1-i)) / 59f * rect.width, rect.yMin + Mathf.Clamp01(Value(core.History[i].sensors) / maximum) * rect.height);
                // 최신 스냅샷은 오른쪽 끝입니다. 실제 수집 이력 아래를 옅게 채웁니다.
                Color fill=new Color(color.r,color.g,color.b,.10f);int area=vh.currentVertCount;
                vh.AddVert(new Vector2(a.x,rect.yMin),fill,Vector2.zero);vh.AddVert(a,fill,Vector2.zero);vh.AddVert(b,fill,Vector2.zero);vh.AddVert(new Vector2(b.x,rect.yMin),fill,Vector2.zero);
                vh.AddTriangle(area,area+1,area+2);vh.AddTriangle(area,area+2,area+3);
                Vector2 n = (b - a).normalized; n = new Vector2(-n.y, n.x) * 1.4f;
                int index = vh.currentVertCount;
                vh.AddVert(a - n, color, Vector2.zero); vh.AddVert(a + n, color, Vector2.zero);
                vh.AddVert(b + n, color, Vector2.zero); vh.AddVert(b - n, color, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index, index + 2, index + 3);
            }
        }
        private float Value(SensorValues s) { return sensor == 0 ? s.motorTemp : sensor == 1 ? s.vibration : sensor == 2 ? s.pressure : s.flow; }
    }
}
