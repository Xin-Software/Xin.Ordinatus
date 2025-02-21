<div align="center">
    <a href="https://www.xin.software"><img src="https://www.xin.software/images/Title1_Dark.png" alt="Xin Software"></a>
</div>

# Xin.Ordinatus

Xin.Ordinatus is a .NET library designed to provide utilities for managing and controlling asynchronous tasks. The library includes mechanisms for rate limiting, debouncing, task queuing, and manual reset events, making it easier to handle concurrency and task execution in a controlled manner.

## Classes

- [AsyncFunnel](#AsyncFunnel)
- [Debouncer](#Debouncer)
- [RateLimiter](#RateLimiter)
- [TaskQueue](#TaskQueue)


### AsyncFunnel
The [AsyncFunnel](src/Xin.Ordinatus/AsyncFunnel.cs) class controls the number of concurrent asynchronous tasks, ensuring that the number of concurrent tasks does not exceed a specified limit.

#### Usage
```csharp
var funnel = new AsyncFunnel(3);

await funnel.RunAsync(async () => {
    // Your task logic here
});
```

#### Methods
> ```csharp
> Task RunAsync(Func<Task> func)
> ```
> Runs the specified asynchronous function, ensuring that the number of concurrent tasks does not exceed the limit.

> ```csharp
> Task<T> RunAsync<T>(Func<Task<T>> func)
> ```
> Runs the specified asynchronous function that returns a result, ensuring that the number of concurrent tasks does not exceed the limit.


### Debouncer
The [Debouncer](src/Xin.Ordinatus/Debouncer.cs) class provides a mechanism to debounce actions, preventing multiple executions of the same action within a specified time period.

#### Usage
```csharp
var debouncer = new Debouncer(TimeSpan.FromMilliseconds(250);
debouncer.Debounce(() => {
    // Your action logic here
});
```

#### Methods
> ```csharp
> void Debounce(Action func)
> ```
> Debounces the specified action, ensuring that it is only executed after the specified delay has elapsed since the last invocation.


### RateLimiter
The [RateLimiter](src/Xin.Ordinatus/RateLimiter) class provides a mechanism to limit the rate at which tasks are executed. It ensures that tasks are executed with a specified time period between them.

#### Usage
```csharp
var rateLimiter = new RateLimiter(TimeSpan.FromSeconds(1));

await rateLimiter.Enqueue(async () => {
    // Your task logic here
});
```

#### Methods
> ```csharp
> Task Enqueue(Func<Task> task, CancellationToken cancellationToken = default)
> ```
> Enqueues a task to be executed by

> ```csharp
> Task<T> Enqueue<T>(Func<Task<T>> task, CancellationToken cancellationToken = default)
> ```
> Enqueues a task that returns a result, ensuring that the rate limit is respected.


### TaskQueue
The [TaskQueue](src/Xin.Ordinatus/TaskQueue.cs) class provides a task queue with a specified maximum number of parallel tasks, ensuring that the number of concurrent tasks does not exceed the specified limit.

#### Usage
```csharp
var options = new TaskQueueOptions { MaxParallelTasks = 4 };
var taskQueue = new TaskQueue(logger, options);

taskQueue.Start(CancellationToken.None);

await taskQueue.Enqueue(async (ct) => {
    // Your task logic here
});

await taskQueue.StopAsync();
```

#### Methods
> ```csharp
> Task Enqueue(Func<CancellationToken, Task> task, CancellationToken cancellationToken = default)
> ```
> Enqueues a task to be executed, ensuring that the maximum number of parallel tasks is not exceeded.

> ```csharp
> void Start(CancellationToken cancellationToken)
> ```
> Starts processing tasks in the queue.

> ```csharp
> Task StopAsync()
> ```
> Stops the task processing in the queue asynchronously.

> ```csharp
> void Pause()
> ```
> Pauses the task processing in the queue.

> ```csharp
> void Resume()
> ```
> Resumes the task processing in the queue.
