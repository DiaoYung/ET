using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace ET.Client
{
    [EntitySystemOf(typeof(RouterAddressComponent))]
    [FriendOf(typeof(RouterAddressComponent))]
    public static partial class RouterAddressComponentSystem
    {
        [EntitySystem]
        private static void Awake(this RouterAddressComponent self, string address, int port)
        {
            self.RouterManagerHost = address;
            self.RouterManagerPort = port;
        }
        
        public static async ETTask Init(this RouterAddressComponent self)
        {
            self.RouterManagerIPAddress = NetworkHelper.GetHostAddress(self.RouterManagerHost);
            await self.GetAllRouter();
        }

        private static async ETTask GetAllRouter(this RouterAddressComponent self)
        {
            string url = $"http://{self.RouterManagerHost}:{self.RouterManagerPort}/get_router?v={RandomGenerator.RandUInt32()}";
            Log.Debug($"start get router info: {url}");
            string routerInfo = await HttpClientHelper.Get(url);
            Log.Debug($"recv router info: {routerInfo}");
            HttpGetRouterResponse httpGetRouterResponse = MongoHelper.FromJson<HttpGetRouterResponse>(routerInfo);
            self.Info = httpGetRouterResponse;
            Log.Debug($"start get router info finish: {MongoHelper.ToJson(httpGetRouterResponse)}");
            
            // 打乱路由器地址的顺序
            RandomGenerator.BreakRank(self.Info.Routers);
            // 等待10分钟后再次获取所有路由器地址
            self.WaitTenMinGetAllRouter().Coroutine();
        }
        
        // 这个方法会等待10分钟后再次获取所有路由器地址
        public static async ETTask WaitTenMinGetAllRouter(this RouterAddressComponent self)
        {
            // 等待5分钟
            await self.Root().GetComponent<TimerComponent>().WaitAsync(10 * 60 * 1000);
            // 如果组件已经被销毁，则直接返回
            if (self.IsDisposed)
            {
                return;
            }
            // 获取所有路由器地址
            await self.GetAllRouter();
        }

        // 这个方法用于获取路由器地址
        public static IPEndPoint GetAddress(this RouterAddressComponent self)
        {
            // 如果没有路由器地址，则返回null
            if (self.Info.Routers.Count == 0)
            {
                return null;
            }
            // 获取路由器地址，并打印日志
            string address = self.Info.Routers[self.RouterIndex++ % self.Info.Routers.Count];
            Log.Info($"get router address: {self.RouterIndex - 1} {address}");
            string[] ss = address.Split(':');
            // 如果是IPv6地址，则转换为IPv6格式
            IPAddress ipAddress = IPAddress.Parse(ss[0]);
            if (self.RouterManagerIPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            { 
                ipAddress = ipAddress.MapToIPv6();
            }
            // 返回IP端点
            return new IPEndPoint(ipAddress, int.Parse(ss[1]));
        }
        
        // 这个方法用于获取Realm地址
        public static IPEndPoint GetRealmAddress(this RouterAddressComponent self, string account)
        {
            // 根据账号计算Realm地址的索引
            int v = account.Mode(self.Info.Realms.Count);
            // 获取Realm地址
            string address = self.Info.Realms[v];
            // 解析IP地址和端口
            string[] ss = address.Split(':');
            IPAddress ipAddress = IPAddress.Parse(ss[0]);
            //if (self.IPAddress.AddressFamily == AddressFamily.InterNetworkV6)
            //{ 
            //    ipAddress = ipAddress.MapToIPv6();
            //}
            // 返回IP端点
            return new IPEndPoint(ipAddress, int.Parse(ss[1]));
        }
    }
}