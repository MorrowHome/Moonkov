// 这些接口让普通场景脚本声明自己要在何时更新。GhostGameObject 收集它们，由对应 ECS 系统在客户端或服务端阶段调用。
public interface IGhostMonoBehaviourUpdate {
}

// 服务端早期更新发生在桥接对象建立之后。需要更新玩法状态或推动物理的脚本，可以选择后面的服务端接口。
public interface IEarlyUpdateServer : IGhostMonoBehaviourUpdate {
    void EarlyUpdateServer(float deltaTime);
}

public interface IUpdateServer : IGhostMonoBehaviourUpdate {
    void UpdateServer(float deltaTime);
}

public interface IPhysicsUpdateServer : IGhostMonoBehaviourUpdate {
    void PhysicsUpdateServer(float deltaTime);
}

// 客户端先接对象和数据、推进客户端模拟，最后更新画面相关的表现。脚本只需实现真正需要参加的阶段。
public interface IEarlyUpdateClient : IGhostMonoBehaviourUpdate {
    void EarlyUpdateClient(float deltaTime);
}

public interface IUpdateClient : IGhostMonoBehaviourUpdate {
    void UpdateClient(float deltaTime);
}

public interface ILateUpdateClient : IGhostMonoBehaviourUpdate {
    void LateUpdateClient(float deltaTime);
}