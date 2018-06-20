轻便作业调度器

# 使用方法

> Install-Package NJob -Version 1.0.1

```csharp
//假设运行在控制台
NJob j = new NJob();

j.Run += (a, b) => {
    string logObj = b.Name;
    System.Net.WebClient wc = new System.Net.WebClient();
    wc.Encoding = Encoding.UTF8;
    string ret = wc.DownloadString(b.RunParam);
    ret = string.Format("{0} {1} 第{3}次执行结果：{2}", DateTime.Now, logObj, ret, b.RunTimes);

    Console.WriteLine(ret);
    Console.Write(DateTime.Now);
};
j.Error += (a, b) => {
    //b.Def.RunParam
    Console.WriteLine("{0} {1} 发生错误：", DateTime.Now, b.Def, b.Exception.Message);
};

j.Start();

Console.WriteLine("...");
Console.ReadKey();
j.Stop();
```

# 配置说明

> 配置文件内容修改后，NJob 会自动加载重新计算定时器

```txt
; 和 # 匀为行注释
;SEC：					按秒触发
;MIN：					按分触发
;HOUR：					按时触发
;DAY：					按天触发
;RunOnDay：				每天 什么时间 触发
;RunOnWeek：			星期几 什么时间 触发
;RunOnMonth：			每月 第几天 什么时间 触发

;Name1		SEC			2				/schedule/test002.aspx
;Name2		MIN			2				/schedule/test002.aspx
;Name3		HOUR		1				/schedule/test002.aspx
;Name4		DAY			2				/schedule/test002.aspx
;Name5		RunOnDay	15:55:59		/schedule/test002.aspx
;每天15点55分59秒
;Name6		RunOnWeek	1:15:55:59		/schedule/test002.aspx
;每星期一15点55分59秒
;Name7		RunOnMonth	1:15:55:59		/schedule/test002.aspx
;每月1号15点55分59秒
```