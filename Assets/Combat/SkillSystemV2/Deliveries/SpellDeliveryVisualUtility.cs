using UnityEngine;

namespace ProjectEri.SkillSystemV2
{
    internal static class SpellDeliveryVisualUtility
    {
        private static Material lineMaterial;

        public static LineRenderer CreateLine(
            GameObject owner,
            Color color,
            float width,
            int sortingOrder,
            bool loop = false)
        {
            LineRenderer line = owner.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.startWidth = Mathf.Max(0.01f, width);
            line.endWidth = Mathf.Max(0.01f, width);
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = sortingOrder;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.material = ResolveLineMaterial();
            return line;
        }

        public static void SetSegment(
            LineRenderer line,
            Vector2 start,
            Vector2 end)
        {
            if (line == null)
                return;

            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        public static void SetCircle(
            LineRenderer line,
            Vector2 center,
            float radius,
            int points = 40)
        {
            if (line == null)
                return;

            int count = Mathf.Max(12, points);
            float safeRadius = Mathf.Max(0.01f, radius);
            line.loop = true;
            line.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                line.SetPosition(
                    i,
                    center + new Vector2(
                        Mathf.Cos(angle),
                        Mathf.Sin(angle)) * safeRadius);
            }
        }

        public static void SanitizeVisualPrefabInstance(GameObject instance)
        {
            if (instance == null)
                return;

            MonoBehaviour[] behaviours =
                instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] != null)
                    behaviours[i].enabled = false;
            }

            Rigidbody2D[] bodies =
                instance.GetComponentsInChildren<Rigidbody2D>(true);
            for (int i = 0; i < bodies.Length; i++)
                bodies[i].simulated = false;

            Collider2D[] colliders =
                instance.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private static Material ResolveLineMaterial()
        {
            if (lineMaterial != null)
                return lineMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
                lineMaterial = new Material(shader);
            return lineMaterial;
        }
    }
}
