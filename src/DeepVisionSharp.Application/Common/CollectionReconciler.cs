using System.Collections.ObjectModel;

namespace DeepVisionSharp.Application.Common;

/// <summary>
/// 将 target 增量调整为 desired 的顺序与内容：同实例（引用相等）原地保留，
/// 仅对新增/移除/位移做最小集合操作，避免 ItemsControl 重建既有容器与界面闪烁。
/// </summary>
public static class CollectionReconciler
{
    public static void Reconcile<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var desiredItem = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], desiredItem))
            {
                continue;
            }

            var moveIndex = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desiredItem))
                {
                    moveIndex = j;
                    break;
                }
            }

            if (moveIndex >= 0)
            {
                target.Move(moveIndex, i);
            }
            else if (i < target.Count)
            {
                target[i] = desiredItem;
            }
            else
            {
                target.Add(desiredItem);
            }
        }
    }
}
