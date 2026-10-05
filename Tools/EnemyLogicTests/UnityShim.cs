// ============================================================================================
// 最小 UnityEngine 替身。
//
// 只为了让 Enemy 模块的**纯逻辑**部分脱离 Unity 编译并运行：
// 状态机、死亡闸门、节流、冲刺行程/撞墙、控制器起手判断。
//
// 这是开发期检查工具，不是游戏代码，也不进 Assets/。
// 它只实现被链接源文件真正用到的成员；如果以后源文件用到新的 Unity API，
// 这里会编译失败并明确指出缺什么 —— 这正是它该有的行为。
//
// 不模拟的游戏侧行为（如物理、协程、序列化）在测试里一律用显式假实现替代。
// ============================================================================================
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name = string.Empty;
        public override string ToString() => name;
    }

    public class Component : Object
    {
        // 惰性创建：Transform 本身也是 Component，如果在字段初始化时 new 就会无限递归。
        private Transform _transform;
        private GameObject _gameObject;

        public Transform transform => _transform ??= new Transform();
        public GameObject gameObject => _gameObject ??= new GameObject();

        // 只实现模块真正会调的那个重载；返回 null 表示「没有该组件」。
        public T GetComponent<T>() where T : class => null;
    }

    public class Behaviour : Component
    {
        public bool enabled = true;
    }

    public class MonoBehaviour : Behaviour
    {
    }

    public class GameObject : Object
    {
        public bool activeSelf = true;
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }

    public static class Debug
    {
        public static int LogCount;
        public static int WarningCount;
        public static int ErrorCount;

        // 测试会读这些计数来断言「报警确实发生了」，而不是只看文案。
        public static string LastError = string.Empty;

        public static void Log(object message)
        {
            LogCount++;

            // 测试需要看到模块内部的探针日志时打开；默认静默以保持输出干净。
            if (EchoLogs)
            {
                Console.WriteLine("      [模块日志] " + message);
            }
        }

        public static bool EchoLogs;

        public static void LogWarning(object message)
        {
            WarningCount++;
        }

        public static void LogError(object message)
        {
            ErrorCount++;
            LastError = message?.ToString() ?? string.Empty;
        }

        public static void ResetCounters()
        {
            LogCount = 0;
            WarningCount = 0;
            ErrorCount = 0;
            LastError = string.Empty;
        }
    }

    public static class Time
    {
        public static float time;
        public static float deltaTime = 1f / 60f;
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);

        public float sqrMagnitude => (x * x) + (y * y) + (z * z);
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);

        public Vector3 normalized
        {
            get
            {
                float m = magnitude;
                return m <= 1e-9f ? zero : new Vector3(x / m, y / m, z / m);
            }
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.x * s, a.y * s, a.z * s);

        public static float Dot(Vector3 a, Vector3 b) => (a.x * b.x) + (a.y * b.y) + (a.z * b.z);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static Quaternion identity => new Quaternion { w = 1f };

        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            // 测试不关心旋转数值，只需一个非默认值以证明「朝向被写过」。
            return new Quaternion { w = 1f, y = forward.x, z = forward.z };
        }

        public static Quaternion Euler(float x, float y, float z)
        {
            return new Quaternion { w = 1f, x = x, y = y, z = z };
        }
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        public static float Abs(float value) => Math.Abs(value);
    }

    // ---- 特性替身 ------------------------------------------------------------------------

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute
    {
    }

    // 配置类（KWC.Data）继承 ScriptableObject 并用 CreateAssetMenu / Min 标注。
    // 测试不加载 .asset，所以这里只需要能编译的最小替身。
    public class ScriptableObject : Object
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName;
        public string fileName;
        public int order;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float min)
        {
        }
    }

    // ---- 物理替身（只为让 PhysicsContactSource 能编译） --------------------------------

    public class Collider : Component
    {
    }

    public class Collision
    {
        public Collider collider => new Collider();
    }
}
