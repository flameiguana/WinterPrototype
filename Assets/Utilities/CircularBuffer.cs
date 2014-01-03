/*
 * Gerardo Perez
 * Circular buffer class supporting traversal via an iterator.
 * */
using System;
using System.Collections;
using System.Collections.Generic;

public class CircularBuffer<T> : IEnumerable<T>
{

	private int Head;
	private int Tail;
	public int Count {get; private set;}

	T[] buffer;

	public CircularBuffer (int length)
	{
		Head = 0;
		Tail = 0;
		Count = 0;
		buffer = new T[length];
	}

	public bool IsEmpty()
	{
		return Count == 0;
	}

	//new elements are added to the end
	public void Add(T o)
	{
		Tail = (Head + Count) % buffer.Length;
		buffer[Tail] = o;
		if(Count == buffer.Length){
			//push down the head, because we overwrote the oldest element
			Head = (Head + 1) % buffer.Length;
		}
		else
			++Count;
	}

	public T ReadOldest()
	{
		return buffer[Head];
	}

	public T ReadNewest()
	{
		return buffer[Tail];
	}

	public T GetByIndex(int distance){
		return buffer[(Head + distance) % buffer.Length];
	}
	//advances head
	public void DiscardOldest()
	{
		Head = (Head + 1) % buffer.Length;
		--Count;
	}
		
	public IEnumerator<T> GetEnumerator()
	{
		int index = Head;
		int accessed = 0;
		while(accessed < Count){
			yield return buffer[index];
			index = (index + 1) % buffer.Length;
			accessed++;
		}
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}
}

