
public abstract class Either<TLeft, TRight>
{
    public static implicit operator Either<TLeft, TRight>(TLeft left) => new Left<TLeft, TRight>(left);
    public static implicit operator Either<TLeft, TRight>(TRight right) => new Right<TLeft, TRight>(right);

    public abstract TResult Match<TResult>(Func<TLeft, TResult> onLeft, Func<TRight, TResult> onRight);

}

public class Left<TLeft, TRight> : Either<TLeft, TRight>
{
    public TLeft Value { get; }

    public Left(TLeft value) => Value = value;

    public override TResult Match<TResult>(Func<TLeft, TResult> onLeft, Func<TRight, TResult> onRight) => onLeft(Value);
}

public class Right<TLeft, TRight> : Either<TLeft, TRight>
{
    public TRight Value { get; }

    public Right(TRight value) => Value = value;

    public override TResult Match<TResult>(Func<TLeft, TResult> onLeft, Func<TRight, TResult> onRight) => onRight(Value);
}