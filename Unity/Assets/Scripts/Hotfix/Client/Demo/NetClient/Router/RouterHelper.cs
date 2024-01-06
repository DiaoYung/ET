using System;
using System.Net;

namespace ET.Client
{
    public static partial class RouterHelper
    {
        // 创建一个路由器会话
        public static async ETTask<Session> CreateRouterSession(this NetComponent netComponent, IPEndPoint address, string account, string password)
        {
            // 生成一个本地连接 ID
            uint localConn = (uint)(account.GetLongHashCode() ^ password.GetLongHashCode() ^ RandomGenerator.RandUInt32());
            // 获取路由器地址
            (uint recvLocalConn, IPEndPoint routerAddress) = await GetRouterAddress(netComponent, address, localConn, 0);

            if (recvLocalConn == 0)
            {
                throw new Exception($"get router fail: {netComponent.Root().Id} {address}");
            }
            
            Log.Info($"get router: {recvLocalConn} {routerAddress}");

            // 创建一个路由器会话，并添加 PingComponent 和 RouterCheckComponent 组件
            Session routerSession = netComponent.Create(routerAddress, address, recvLocalConn);
            routerSession.AddComponent<PingComponent>();
            routerSession.AddComponent<RouterCheckComponent>();
            
            return routerSession;
        }
        
        /// <summary>
        /// GetRouterAddress 方法用于获取路由器的地址。它首先从 Root 组件中获取 RouterAddressComponent 组件，
        /// 然后调用 RouterAddressComponent 的 GetAddress 方法获取路由器的地址。
        /// 接着，它调用 Connect 方法尝试连接到路由器，并获取本地连接 ID。
        /// 最后，它返回本地连接 ID 和路由器的地址。
        /// </summary>
        /// <param name="netComponent"></param>
        /// <param name="address"></param>
        /// <param name="localConn"></param>
        /// <param name="remoteConn"></param>
        /// <returns></returns>
        public static async ETTask<(uint, IPEndPoint)> GetRouterAddress(this NetComponent netComponent, IPEndPoint address, uint localConn, uint remoteConn)
        {
            Log.Info($"start get router address: {netComponent.Root().Id} {address} {localConn} {remoteConn}");
            //return (RandomHelper.RandUInt32(), address);
            RouterAddressComponent routerAddressComponent = netComponent.Root().GetComponent<RouterAddressComponent>();
            IPEndPoint routerInfo = routerAddressComponent.GetAddress();

            uint recvLocalConn = await netComponent.Connect(routerInfo, address, localConn, remoteConn);
            
            Log.Info($"finish get router address: {netComponent.Root().Id} {address} {localConn} {remoteConn} {recvLocalConn} {routerInfo}");
            return (recvLocalConn, routerInfo);
        }

        /// <summary>
        /// Connect 方法用于连接到路由器。它首先根据 remoteConn 是否为 0 来设置同步标志，
        /// 然后创建一个 RouterConnector 对象，并设置重试次数和发送缓冲区。
        /// 接着，它设置连接 ID 和发送数据，并循环发送连接请求，直到连接成功或超时。
        /// 最后，它返回本地连接 ID。
        /// </summary>
        /// <param name="netComponent"></param>
        /// <param name="routerAddress"></param>
        /// <param name="realAddress"></param>
        /// <param name="localConn"></param>
        /// <param name="remoteConn"></param>
        /// <returns></returns>
        private static async ETTask<uint> Connect(this NetComponent netComponent, IPEndPoint routerAddress, IPEndPoint realAddress, uint localConn, uint remoteConn)
        {
            byte synFlag = remoteConn == 0? KcpProtocalType.RouterSYN : KcpProtocalType.RouterReconnectSYN;

            // 注意，session也以localConn作为id，所以这里不能用localConn作为id
            long id = (long)(((ulong)localConn << 32) | remoteConn);
            using RouterConnector routerConnector = netComponent.AddChildWithId<RouterConnector>(id);
            
            int count = 20;
            // 创建一个 512 字节的缓冲区，用于存储要发送的数据
            byte[] sendCache = new byte[512];

            // 生成一个随机的连接 ID
            uint connectId = RandomGenerator.RandUInt32();
            // 将同步标志（synFlag）、本地连接 ID（localConn）、远程连接 ID（remoteConn）和刚刚生成的连接 ID（connectId）写入缓冲区的指定位置
            sendCache.WriteTo(0, synFlag);
            sendCache.WriteTo(1, localConn);
            sendCache.WriteTo(5, remoteConn);
            sendCache.WriteTo(9, connectId);
            // 将真实的地址（realAddress）转换为字节数组，并复制到缓冲区的指定位置
            byte[] addressBytes = realAddress.ToString().ToByteArray();
            Array.Copy(addressBytes, 0, sendCache, 13, addressBytes.Length);
            // 获取 TimerComponent 组件，这个组件可能会在后续的代码中用于定时任务
            TimerComponent timerComponent = netComponent.Root().GetComponent<TimerComponent>();
            Log.Info($"router connect: {localConn} {remoteConn} {routerAddress} {realAddress}");

            long lastSendTimer = 0;

            while (true)
            {
                long timeNow = TimeInfo.Instance.ClientFrameTime();
                if (timeNow - lastSendTimer > 300)
                {
                    if (--count < 0)
                    {
                        Log.Error($"router connect timeout fail! {localConn} {remoteConn} {routerAddress} {realAddress}");
                        return 0;
                    }

                    lastSendTimer = timeNow;
                    // 发送
                    routerConnector.Connect(sendCache, 0, addressBytes.Length + 13, routerAddress);
                }

                await timerComponent.WaitFrameAsync();
                
                if (routerConnector.Flag == 0)
                {
                    continue;
                }

                return localConn;
            }
        }
    }
}