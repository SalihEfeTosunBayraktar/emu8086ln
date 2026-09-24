; EXE program with separate data, stack and code segments.
#make_EXE#

data segment
    greeting db 'Data lives in its own segment.', 13, 10, '$'
ends

stack segment
    dw 128 dup(0)
ends

code segment
start:
    mov ax, data            ; the loader fixes up the segment address
    mov ds, ax

    mov dx, offset greeting
    mov ah, 09h
    int 21h

    mov ax, 4C00h
    int 21h
ends

end start
