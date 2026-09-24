; FAR procedures in another code segment: CALL FAR pushes CS and IP, RETF pops both.
#make_EXE#

data segment
    msg1 db 'In the main segment', 13, 10, '$'
    msg2 db 'In the library segment', 13, 10, '$'
ends

stack segment
    dw 128 dup(0)
ends

library segment
print_far proc far
    mov ah, 09h
    int 21h
    retf                    ; FAR return: IP and CS come from the stack
print_far endp
ends

code segment
start:
    mov ax, data
    mov ds, ax
    mov dx, offset msg1
    mov ah, 09h
    int 21h
    mov dx, offset msg2
    call far ptr print_far  ; watch CS change in the registers panel
    mov ax, 4C00h
    int 21h
ends

end start
