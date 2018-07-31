using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework.Services.System;

namespace TokenContract
{
    public class TokenContract : SmartContract
    {

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;

        //管理员账户 admin account
        //testnet账户  AaBmSJ4Beeg2AeKczpXk89DnmVrPn3SHkU
        private static readonly byte[] admin = Helper.ToScriptHash("AZ77FiX7i9mRUPF2RyuJD2L8kS6UDnQ9Y7");

        //nep5 func
        public static BigInteger totalSupply()
        {
            return Storage.Get(Storage.CurrentContext, TOTAL_SUPPLY).AsBigInteger();
        }

        public static string name()
        {
            return "Standards";
        }

        public static string symbol()
        {
            return "SDS";
        }
        //因子 
        //factor
        private const ulong factor = 100000000;

        //总计数量 
        //total amount
        private const ulong TOTAL_AMOUNT = 1000000000 * factor;

        private const string TOTAL_SUPPLY = "totalSupply";

        public static byte decimals()
        {
            return 8;
        }


        /// <summary>
        ///   This smart contract is designed to implement NEP-5
        ///   Parameter List: 0710
        ///   Return List: 05
        /// </summary>
        /// <param name="operation">
        ///     The methos being invoked.
        /// </param>
        /// <param name="args">
        ///     Optional input parameters used by NEP5 methods.
        /// </param>
        /// <returns>
        ///     Return Object
        /// </returns>
        public static Object Main(string operation, params object[] args)
        {
            var magicstr = "2018-07-31";

            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(admin);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                //入口函数取得callscript，获取调用地址
                //get callscript from main
                var callscript = ExecutionEngine.CallingScriptHash;
                //this is in nep5
                if (operation == "totalSupply") return totalSupply();
                if (operation == "name") return name();
                if (operation == "symbol") return symbol();
                if (operation == "decimals") return decimals();
                if (operation == "balanceOf")
                {
                    if (args.Length != 1) return 0;
                    byte[] account = (byte[])args[0];
                    if (account.Length != 20)
                        return false;
                    return balanceOf(account);
                }
                if (operation == "init")
                {
                    return init();
                }
                if (operation == "transfer")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];

                    if (!Runtime.CheckWitness(from))
                        return false;
                    return transfer(from, to, value);
                }
                //允许合约调用
                //allow contract invoke
                if (operation == "transfer_contract")
                {
                    if (args.Length != 3) return false;
                    byte[] from = (byte[])args[0];
                    byte[] to = (byte[])args[1];
                    BigInteger value = (BigInteger)args[2];

                    //判断调用者是否是合约调用
                    //check invoke is jump contract
                    if (callscript.AsBigInteger() != from.AsBigInteger())
                        return false;
                    return transfer(from, to, value);
                }
                if (operation == "getTXInfo")
                {
                    if (args.Length != 1) return 0;
                    byte[] txid = (byte[])args[0];
                    return getTXInfo(txid);
                }
            }
            return false;
        }


        /// <summary>
        ///  Get the balance of the address
        /// </summary>
        /// <param name="address">
        ///  address
        /// </param>
        /// <returns>
        ///   account balance
        /// </returns>
        public static BigInteger balanceOf(byte[] address)
        {
            var keyAddress = new byte[] { 0x11 }.Concat(address);
            return Storage.Get(Storage.CurrentContext, keyAddress).AsBigInteger();
        }

        /// <summary>
        ///   Transfer a token balance to another account.
        /// </summary>
        /// <param name="from">
        ///   The contract invoker.
        /// </param>
        /// <param name="to">
        ///   The account to transfer to.
        /// </param>
        /// <param name="value">
        ///   The amount to transfer.
        /// </param>
        /// <returns>
        ///   Transaction Successful?
        /// </returns>
        public static bool transfer(byte[] from, byte[] to, BigInteger value)
        {
            if (from.Length != 20 || to.Length != 20)
                return false;

            if (value <= 0) return false;

            if (from == to) return true;

            //付款方
            if (from.Length > 0)
            {
                var keyFrom = new byte[] { 0x11 }.Concat(from);
                BigInteger from_value = Storage.Get(Storage.CurrentContext, keyFrom).AsBigInteger();
                if (from_value < value) return false;
                if (from_value == value)
                    Storage.Delete(Storage.CurrentContext, keyFrom);
                else
                    Storage.Put(Storage.CurrentContext, keyFrom, from_value - value);
            }
            //收款方
            if (to.Length > 0)
            {
                var keyTo = new byte[] { 0x11 }.Concat(to);
                BigInteger to_value = Storage.Get(Storage.CurrentContext, keyTo).AsBigInteger();
                Storage.Put(Storage.CurrentContext, keyTo, to_value + value);
            }
            //记录交易信息
            setTxInfo(from, to, value);
            //notify
            Transferred(from, to, value);
            return true;
        }

        public static TransferInfo getTXInfo(byte[] txid)
        {
            byte[] v = Storage.Get(Storage.CurrentContext, new byte[] { 0x12 }.Concat(txid));
            if (v.Length == 0)
                return null;

            return (TransferInfo)Helper.Deserialize(v);
        }

        private static void setTxInfo(byte[] from, byte[] to, BigInteger value)
        {
            TransferInfo info = new TransferInfo();
            info.from = from;
            info.to = to;
            info.value = value;

            byte[] txinfo = Helper.Serialize(info);

            var txid = (ExecutionEngine.ScriptContainer as Transaction).Hash;
            var keytxid = new byte[] { 0x12 }.Concat(txid);
            Storage.Put(Storage.CurrentContext, keytxid, txinfo);
        }

        static readonly byte[] doublezero = new byte[2] { 0x00, 0x00 };

        public class TransferInfo
        {
            public byte[] from;
            public byte[] to;
            public BigInteger value;
        }

        /// <summary>
        ///   Init the sdt tokens to the SuperAdmin account，only once
        /// </summary>
        /// <returns>
        ///   Transaction Successful?
        /// </returns>
        public static bool init()
        {
            if (!Runtime.CheckWitness(admin)) return false;
            byte[] total_supply = Storage.Get(Storage.CurrentContext, TOTAL_SUPPLY);
            if (total_supply.Length != 0) return false;
            Storage.Put(Storage.CurrentContext, new byte[] { 0x11 }.Concat(admin), IntToBytes(TOTAL_AMOUNT));
            Storage.Put(Storage.CurrentContext, TOTAL_SUPPLY, TOTAL_AMOUNT);
            Transferred(null, admin, TOTAL_AMOUNT);
            return true;
        }

        private static byte[] IntToBytes(BigInteger value)
        {
            byte[] buffer = value.ToByteArray();
            return buffer;
        }

        private static BigInteger BytesToInt(byte[] array)
        {
            var buffer = new BigInteger(array);
            return buffer;
        }

    }
}
