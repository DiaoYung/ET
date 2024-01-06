using System;
using System.Net;
using System.Net.Sockets;

namespace ET.Client
{
    [MessageHandler(SceneType.NetClient)]
    public class Main2NetClient_LoginHandler: MessageHandler<Scene, Main2NetClient_Login, NetClient2Main_Login>
    {
        // 定义一个异步方法 Run，该方法用于处理客户端的登录请求
        protected override async ETTask Run(Scene root, Main2NetClient_Login request, NetClient2Main_Login response)
        {
            // 从请求中获取账号和密码
            string account = request.Account;
            string password = request.Password;

            // 从 root 场景中移除任何已存在的 RouterAddressComponent            
            root.RemoveComponent<RouterAddressComponent>();
            // 向 root 场景中添加一个新的 RouterAddressComponent，并设置其 HTTP 主机和端口
            RouterAddressComponent routerAddressComponent =
                    root.AddComponent<RouterAddressComponent, string, int>(ConstValue.RouterHttpHost, ConstValue.RouterHttpPort);
            // 初始化 RouterAddressComponent        
            await routerAddressComponent.Init();
            // 向 root 场景中添加一个 NetComponent，用于网络通信    
            root.AddComponent<NetComponent, AddressFamily, NetworkProtocol>(routerAddressComponent.RouterManagerIPAddress.AddressFamily, NetworkProtocol.UDP);
            // 设置 FiberParentComponent 的 ParentFiberId    
            root.GetComponent<FiberParentComponent>().ParentFiberId = request.OwnerFiberId;
            // 获取 NetComponent
            NetComponent netComponent = root.GetComponent<NetComponent>();
            // 从 RouterAddressComponent 中获取 Realm 服务器的地址
            IPEndPoint realmAddress = routerAddressComponent.GetRealmAddress(account);
            // 创建一个与 Realm 服务器的会话，并发送一个 C2R_Login 请求，此处是长连接，using语句会自动释放会话
            R2C_Login r2CLogin;
            using (Session session = await netComponent.CreateRouterSession(realmAddress, account, password))
            {
                r2CLogin = (R2C_Login)await session.Call(new C2R_Login() { Account = account, Password = password });
            }

            // 创建一个与 Gate 服务器的会话，并保存到 SessionComponent 中
            Session gateSession = await netComponent.CreateRouterSession(NetworkHelper.ToIPEndPoint(r2CLogin.Address), account, password);
            gateSession.AddComponent<ClientSessionErrorComponent>();
            root.AddComponent<SessionComponent>().Session = gateSession;
            // 向 Gate 服务器发送一个 C2G_LoginGate 请求    
            G2C_LoginGate g2CLoginGate = (G2C_LoginGate)await gateSession.Call(new C2G_LoginGate() { Key = r2CLogin.Key, GateId = r2CLogin.GateId });

            Log.Debug("登陆gate成功!");
            // 将玩家 ID 设置到响应中，该响应将会被发送回客户端
            response.PlayerId = g2CLoginGate.PlayerId;
        }
    }
}