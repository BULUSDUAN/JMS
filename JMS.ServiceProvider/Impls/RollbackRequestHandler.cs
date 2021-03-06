﻿using JMS.Dtos;
using JMS.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using Way.Lib;

namespace JMS.Impls
{
    class RollbackRequestHandler : IRequestHandler
    {
        TransactionDelegateCenter _transactionDelegateCenter;
        public RollbackRequestHandler(TransactionDelegateCenter transactionDelegateCenter)
        {
            _transactionDelegateCenter = transactionDelegateCenter;
        }

        public InvokeType MatchType => InvokeType.RollbackTranaction;

        public void Handle(NetClient netclient, InvokeCommand cmd)
        {
           var ret = _transactionDelegateCenter.Rollback(cmd.Header["TranId"]);
            netclient.WriteServiceData(new InvokeResult() { Success = ret });
        }
    }
}
