namespace CashFlow.SharedKernel.Application;

/// <summary>Marker for anything the application layer can execute and that yields <typeparamref name="TResponse"/>.</summary>
public interface IRequest<TResponse>;

/// <summary>A state-changing request.</summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>A side-effect-free request.</summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
