// 这些接口让普通场景脚本声明自己要在何时更新。GhostGameObject 收集它们，由对应 ECS 系统在客户端或服务端阶段调用。
public interface IGhostMonoBehaviourUpdate {}

public interface IEarlyUpdateServer : IGhostMonoBehaviourUpdate
{
    void EarlyUpdateServer(float deltaTime);
}

public interface IUpdateServer : IGhostMonoBehaviourUpdate
{
    void UpdateServer(float deltaTime);
}

public interface IPhysicsUpdateServer : IGhostMonoBehaviourUpdate
{
    void PhysicsUpdateServer(float deltaTime);
}

public interface IEarlyUpdateClient : IGhostMonoBehaviourUpdate
{
    void EarlyUpdateClient(float deltaTime);
}

public interface IUpdateClient : IGhostMonoBehaviourUpdate
{
    void UpdateClient(float deltaTime);
}

public interface ILateUpdateClient : IGhostMonoBehaviourUpdate
{
    void LateUpdateClient(float deltaTime);
}
