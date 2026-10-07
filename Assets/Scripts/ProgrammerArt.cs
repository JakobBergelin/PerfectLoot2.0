using UnityEngine;
using Unity.Mathematics;
using UnityEditor;

public class ProgrammerArt
{
    public static void DrawBox(float3 center, quaternion rotation, float3 size, Color color = default, float duration = 0f)
    {
        if (color.Equals(default)) color = Color.red;

        var matrix = float4x4.TRS(center, rotation, 1);
        //front corners
        var forwardRightUpper = math.transform(matrix, new float3(size.x / 2, size.y / 2, size.z / 2));
        var forwardRightLower = math.transform(matrix, new float3(size.x / 2, -size.y / 2, size.z / 2));
        var forwardLeftUpper = math.transform(matrix, new float3(-size.x / 2, size.y / 2, size.z / 2));
        var forwardLeftLower = math.transform(matrix, new float3(-size.x / 2, -size.y / 2, size.z / 2));
        //back corners
        var backRightUpper = math.transform(matrix, new float3(size.x / 2, size.y / 2, -size.z / 2));
        var backRightLower = math.transform(matrix, new float3(size.x / 2, -size.y / 2, -size.z / 2));
        var backLeftUpper = math.transform(matrix, new float3(-size.x / 2, size.y / 2, -size.z / 2));
        var backLeftLower = math.transform(matrix, new float3(-size.x / 2, -size.y / 2, -size.z / 2));
        //square front
        Debug.DrawLine(forwardRightUpper, forwardRightLower, color, duration);
        Debug.DrawLine(forwardRightUpper, forwardLeftUpper, color, duration);
        Debug.DrawLine(forwardRightLower, forwardLeftLower, color, duration);
        Debug.DrawLine(forwardLeftLower, forwardLeftUpper, color, duration);
        //square back
        Debug.DrawLine(backRightUpper, backRightLower, color, duration);
        Debug.DrawLine(backRightUpper, backLeftUpper, color, duration);
        Debug.DrawLine(backRightLower, backLeftLower, color, duration);
        Debug.DrawLine(backLeftLower, backLeftUpper, color);
        //attach squares
        Debug.DrawLine(backRightUpper, forwardRightUpper, color, duration);
        Debug.DrawLine(backLeftUpper, forwardLeftUpper, color, duration);
        Debug.DrawLine(backRightLower, forwardRightLower, color, duration);
        Debug.DrawLine(backLeftLower, forwardLeftLower, color, duration);
    }

    public static void DrawSphere(
        Vector3 center,
        float radius,
        Color color,
        int segments = 8,
        float duration = 0f)
    {
        float step = 360f / segments;

        // Latitude circles
        for (int i = 0; i <= segments; i++)
        {
            float latitude = Mathf.Lerp(-90f, 90f, i / (float)segments);
            DrawCircle(center, radius, latitude, true, color, segments, duration);
        }

        // Longitude circles
        for (int i = 0; i < segments; i++)
        {
            float longitude = i * step;
            DrawCircle(center, radius, longitude, false, color, segments, duration);
        }
    }

    private static void DrawCircle(
        Vector3 center,
        float radius,
        float angle,
        bool isLatitude,
        Color color,
        int segments,
        float duration)
    {
        Vector3 prevPoint = Vector3.zero;
        float step = 360f / segments;

        for (int i = 0; i <= segments; i++)
        {
            float theta = i * step * Mathf.Deg2Rad;
            float phi = angle * Mathf.Deg2Rad;

            Vector3 point;

            if (isLatitude)
            {
                point = new Vector3(
                    Mathf.Cos(phi) * Mathf.Cos(theta),
                    Mathf.Sin(phi),
                    Mathf.Cos(phi) * Mathf.Sin(theta)
                );
            }
            else
            {
                point = new Vector3(
                    Mathf.Cos(theta) * Mathf.Cos(phi),
                    Mathf.Sin(theta),
                    Mathf.Cos(theta) * Mathf.Sin(phi)
                );
            }

            point = center + point * radius;

            if (i > 0)
            {
                Debug.DrawLine(prevPoint, point, color, duration);
            }

            prevPoint = point;
        }
    }
}
