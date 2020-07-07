﻿using JMS.Common.Dtos;
using JMS.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Way.Lib;

namespace JMS
{
    class GatewayRefereeClient
    {
        NetAddress _refereeAddress;
        ILogger<GatewayRefereeClient> _logger;
        LockKeyManager _lockKeyManager;
        ConcurrentDictionary<string, RegisterServiceInfo> _waitServiceList;
        Gateway _gateway;
        /// <summary>
        /// 记录当前网关是否是master
        /// </summary>
        public bool IsMaster { get; private set; }
        public GatewayRefereeClient(IConfiguration configuration,
            LockKeyManager lockKeyManager,
            Gateway gateway,
            ILogger<GatewayRefereeClient> logger)
        {
            _refereeAddress = configuration.GetSection("Cluster:Referee").Get<NetAddress>();
            _logger = logger;
            _lockKeyManager = lockKeyManager;
            _gateway = gateway;

            if (_refereeAddress == null)
            {
                _lockKeyManager.IsReady = true;
                this.IsMaster = true;
            }
            else
                new Thread(toBeMaster).Start();

            SystemEventCenter.MicroServiceUploadLockedKeyCompleted += SystemEventCenter_MicroServiceUploadLockedKeyCompleted;
        }

        private void SystemEventCenter_MicroServiceUploadLockedKeyCompleted(object sender, RegisterServiceInfo e)
        {
            _waitServiceList?.TryRemove(e.ServiceId,out RegisterServiceInfo o);
        }

        void keepAlive(NetClient netclient)
        {
            while (true)
            {
                try
                {
                    netclient.WriteServiceData(new GatewayCommand());
                    netclient.ReadServiceData();
                }
                catch(Exception ex)
                {
                    netclient.Dispose();
                    _logger?.LogError(ex, "网关连接断开");
                    _lockKeyManager.IsReady = false;
                    this.IsMaster = false;

                    //断开所有微服务
                    var list = _gateway.OnlineMicroServices.ToArray();
                    foreach (var service in list)
                        service.Close();
                }
            }
        }

        void toBeMaster()
        {
            while(true)
            {              
                try
                {
                    if (!this.IsMaster)
                    {
                        _lockKeyManager.IsReady = false;
                    }

                    using (var client = new NetClient(_refereeAddress))
                    {
                        client.WriteServiceData(new GatewayCommand { 
                            Type = CommandType.ApplyToBeMaster
                        });
                        var ret = client.ReadServiceObject<InvokeResult<ConcurrentDictionary<string, RegisterServiceInfo>>>();


                        if(ret.Success)
                        {
                            if(this.IsMaster == false)
                            {
                                _waitServiceList = ret.Data;
                                this.IsMaster = true;

                                //等待所有微服务上传locked key
                                for (int i = 0; i < 10 && _waitServiceList.Count > 0; i++)
                                    Thread.Sleep(1000);
                                _lockKeyManager.IsReady = true;

                                keepAlive(client);
                            }                           
                        }

                        client.Dispose();
                    }
                }
                catch(SocketException)
                {
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, ex.Message);
                }
                Thread.Sleep(2000);
            }
        }

        public void AddMicroService(RegisterServiceInfo service)
        {
            if (_refereeAddress == null)
                return;
            using (NetClient client = new NetClient(_refereeAddress))
            {
                client.WriteServiceData(new GatewayCommand
                {
                    Type = CommandType.RegisterSerivce,
                    Content = service.ToJsonString()
                });
                var cmd = client.ReadServiceObject<InvokeResult>();
                if (cmd.Success == false)
                    throw new Exception("not master");
            }
        }

        public void RemoveMicroService(RegisterServiceInfo service)
        {
            if (_refereeAddress == null)
                return;

            Task.Run(() =>
            {
                try
                {
                    using (NetClient client = new NetClient(_refereeAddress.Address, _refereeAddress.Port))
                    {
                        client.WriteServiceData(new GatewayCommand
                        {
                            Type = CommandType.UnRegisterSerivce,
                            Content = service.ToJsonString()
                        });
                        var cmd = client.ReadServiceObject<InvokeResult>();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, ex.Message);

                }
            });
        }
    }
}
