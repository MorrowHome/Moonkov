// 定义按 tick 标记的状态缓冲，以及覆盖同 tick、查指定 tick 之前最近一条等小工具。想看实际每拍怎样取输入和推进角色，从 PlayerPredictionSystem 开始。
using Unity.Entities;

public interface IPredictedState : IBufferElementData {
    uint Tick { get; set; }
}

public static class SequenceHelpers {
    // tick 用无符号整数，长时间运行会绕回零。用差值位于半圈的哪一侧来认新旧，比直接比较两个数字适合这种编号。
    public static bool IsNewer(uint current, uint old) { return !(old - current < 1u << 31); }
}

public static class PredictedStateUtility {
    public const int maxPredictedTicks = 64;

    public static bool GetStateAtTick<T>(this DynamicBuffer<T> stateArray, uint targetTick, out T state)
        where T : unmanaged, IPredictedState {
        // 指定 tick 恰好没有记录时，遍历仍会挑它之前最近的一条。没有任何更早记录才返回失败。
        int beforeIdx = 0;
        uint beforeTick = 0;
        for (int i = 0; i < stateArray.Length; ++i) {
            uint tick = stateArray[i].Tick;
            if (!SequenceHelpers.IsNewer(tick, targetTick) &&
                (beforeTick == 0 || SequenceHelpers.IsNewer(tick, beforeTick))) {
                beforeIdx = i;
                beforeTick = tick;
            }
        }
        if (beforeTick == 0) {
            state = default;
            return false;
        }
        state = stateArray[beforeIdx];
        return true;
    }

    public static void AddState<T>(this DynamicBuffer<T> stateArray, T state) where T : unmanaged, IPredictedState {
        uint targetTick = state.Tick;
        int oldestIdx = 0;
        uint oldestTick = 0;
        for (int i = 0; i < stateArray.Length; ++i) {
            uint tick = stateArray[i].Tick;
            // 重复预测同一拍会替换旧记录。缓冲最多留固定数量，放不下时用这一拍盖掉最老的条目。
            if (tick == targetTick) {
                // 找到同一 tick 时覆盖已有记录。
                stateArray[i] = state;
                return;
            }
            if (oldestTick == 0 || SequenceHelpers.IsNewer(oldestTick, tick)) {
                oldestIdx = i;
                oldestTick = tick;
            }
        }
        if (stateArray.Length < maxPredictedTicks)
            stateArray.Add(state);
        else
            stateArray[oldestIdx] = state;
    }

    public static uint GetNewestTick<T>(this DynamicBuffer<T> stateArray) where T : unmanaged, IPredictedState {
        uint newestTick = 0;
        for (int i = 0; i < stateArray.Length; ++i) {
            uint tick = stateArray[i].Tick;
            if (newestTick == 0 || SequenceHelpers.IsNewer(tick, newestTick)) newestTick = tick;
        }
        return newestTick;
    }
}